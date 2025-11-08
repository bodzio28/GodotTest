# Good article about the EOS login flows: https://eoshelp.epicgames.com/s/article/What-is-the-correct-login-flow-for-a-game-that-supports-crossplay
extends Node

#region Signals

## Emitted when the user logs in to Epic Game Services. If the [auto_connect_account] option is false, this will also be emitted when the user logs in to Epic Account Services
signal logged_in

## Emitted when the user logs out of Epic Game Services
signal logged_out

## Emitted when an error occurs while loggin in
signal login_error(result_code: EOS.Result)

## Related to Epic Account Services
signal logged_in_auth

## Related to Epic Account Services
signal logged_out_auth

## Related to Epic Account Services
signal login_auth_error(result_code: EOS.Result)

## Related to Epic Game Services
signal logged_in_connect

## Related to Epic Game Services
signal logged_out_connect

## Related to Epic Game Services
signal login_connect_error(result_code: EOS.Result)

## Emitted when the display name is changed. Get the display name using HAuth.display_name.
## When user logs in, this event will be emitted when we receive the user's display name.
## When user logs out, this event will be emitted as display name will be empty string.
signal display_name_changed

## Emitted when the external account info is changed.
## When user logs in, this event will be emitted when we receive the user's external account info.
## When user logs out, this event will be emitted as external account info will be empty.
signal external_account_info_changed

## Emitted when we positively verify (extra step) że token użytkownika jest ważny po zakończeniu logowania w przeglądarce
signal login_verified

## Emitted gdy dodatkowa weryfikacja tokenu się nie uda (np. token wygasł albo użytkownik zamknął okno bez akceptacji)
signal login_verification_failed(result_code: EOS.Result)

## Emitted whenever EOS provides/refreshes the Device Code (PIN grant) during Account Portal login
## Carries the short code and the verification URL (complete if available)
signal pin_grant_updated(code: String, verification_uri: String)

#endregion


#region Public vars

## The epic account id of the logged in user (Used for Epic Account Services)
var epic_account_id := ""

## The product user id of the logged in user (Used for Epic Game Services)
var product_user_id := ""

## The display name of the logged in user
var display_name: String


## Whether to automatically fetch the external account linked with Epic Game Services (default true)
var auto_fetch_external_account := true

## The external account linked with Epic Game Services
## See [method get_external_account_by_type_async] for return type
var external_account_info := {}


## Whether to automatically link an epic account for external identity provider (default true)
var auto_link_account := true

## Whether to automatically login to Epic Game Services after logging in to Epic Account Services (default true)
var auto_connect_account := true

## Default scope flags used when logging in with Epic Account Services[br]
## Flags from [enum EOS.Auth.ScopeFlags]
var auth_login_scope_flags: int = EOS.Auth.ScopeFlags.BasicProfile | EOS.Auth.ScopeFlags.Presence | EOS.Auth.ScopeFlags.FriendsList

## Default login flags used when logging in with Epic Account Services.[br]
## Flags from [enum EOS.Auth.LoginFlags]
var auth_login_flags: int = EOS.Auth.LoginFlags.None

#endregion


#region Private vars 

var _log = HLog.logger("HAuth")
var _verify_callback_connected := false
var _pin_grant_browser_opened := false # Guard żeby nie otwierać wielokrotnie tej samej strony PIN/device code
var _last_pin_code := "" # Ostatnio otrzymany kod PIN z AuthPinGrant, aby odświeżać UI tylko przy zmianie
# Bieżące publicznie dostępne wartości PIN/device code (udostępniane aplikacji C#/GDScript przez właściwości)
var pin_grant_code_current: String = ""
var pin_grant_verification_uri_current: String = ""

#endregion


#region Built-in methods

func _ready() -> void:
	IEOS.connect_interface_auth_expiration.connect(_on_connect_interface_auth_expiration)

#endregion


#region Public methods


## Login using the Epic Dev Auth tool
func login_devtool_async(server_url: String, credential_name: String) -> bool:
	_log.debug("Logging in using Epic Dev Auth tool...")
	var opts = EOS.Auth.LoginOptions.new()
	opts.credentials = EOS.Auth.Credentials.new()
	opts.credentials.type = EOS.Auth.LoginCredentialType.Developer
	opts.credentials.id = server_url
	opts.credentials.token = credential_name
	opts.scope_flags = auth_login_scope_flags
	opts.login_flags = auth_login_flags

	return await login_async(opts)


## Login using Epic Account Portal
func login_account_portal_async() -> bool:
	_log.info("=== ROZPOCZYNAM NOWĄ SESJĘ LOGOWANIA PRZEZ ACCOUNT PORTAL ===")
	
	# Preflight: inform if required scopes are not included (helps z diagnozą error.invalidClient / missing scopes)
	var required_scopes = EOS.Auth.ScopeFlags.BasicProfile | EOS.Auth.ScopeFlags.Presence | EOS.Auth.ScopeFlags.FriendsList
	if (auth_login_scope_flags & required_scopes) != required_scopes:
		_log.debug("Auth scopes missing; ustawiam BasicProfile|Presence|FriendsList dla Account Portal")
		auth_login_scope_flags = required_scopes
	
	# PEŁNY RESET stanu PIN grant dla nowej sesji loginu
	_pin_grant_browser_opened = false
	_last_pin_code = ""
	pin_grant_code_current = ""
	pin_grant_verification_uri_current = ""
	_log.info("Zresetowano stan PIN grant (browser_opened=false, last_code='', current_code='', current_uri='')")
	
	# Jeśli jesteśmy już zalogowani, najpierw wyloguj aby SDK wygenerował nowy kod
	if epic_account_id:
		_log.info("Wykryto istniejący epic_account_id='%s' - wymuszam wylogowanie aby SDK wygenerował nowy Device Code" % epic_account_id)
		await logout_async()
		# Krótkie opóźnienie aby SDK oczyścił stan
		await get_tree().create_timer(0.5).timeout
	
	var opts = EOS.Auth.LoginOptions.new()
	opts.credentials = EOS.Auth.Credentials.new()
	opts.credentials.type = EOS.Auth.LoginCredentialType.AccountPortal
	opts.scope_flags = auth_login_scope_flags
	opts.login_flags = EOS.Auth.LoginFlags.None  # Wyraźnie ustaw na None przy pierwszym logowaniu
	
	_log.info("Wywołuję login_async z AccountPortal credentials...")
	return await login_async(opts)


## Dodatkowa asynchroniczna weryfikacja po logowaniu przez przeglądarkę (AccountPortal / DeviceCode).
## Używa verify_user_auth jeśli dostępny sygnał callback; w przeciwnym razie sprawdza status logowania.
## Zwraca true jeśli użytkownik jest zalogowany i token zweryfikowany.
func verify_browser_login_async(timeout_sec := 15.0) -> bool:
	# Musimy mieć epic_account_id aby cokolwiek weryfikować
	if not epic_account_id:
		_log.debug("verify_browser_login_async: Brak epic_account_id – użytkownik nie jest zalogowany")
		login_verification_failed.emit(EOS.Result.InvalidAuth)
		return false

	# Pobierz token użytkownika
	var copy_ret = EOS.Auth.AuthInterface.copy_user_auth_token(EOS.Auth.CopyUserAuthTokenOptions.new(), epic_account_id)
	if not EOS.is_success(copy_ret):
		_log.error("verify_browser_login_async: Nie udało się pobrać tokenu użytkownika: %s" % EOS.result_str(copy_ret))
		login_verification_failed.emit(copy_ret.result_code)
		return false

	var token: EOS.Auth.Token = copy_ret.token
	if not token or not token.access_token:
		_log.error("verify_browser_login_async: token pusty")
		login_verification_failed.emit(EOS.Result.InvalidAuth)
		return false

	# Jeśli SDK wystawia sygnał verify callback (nazwa wg konwencji), użyjemy go; w przeciwnym razie fallback na status
	var has_signal := IEOS.has_signal("auth_interface_verify_user_auth_callback")
	if has_signal:
		if not _verify_callback_connected:
			IEOS.auth_interface_verify_user_auth_callback.connect(_on_verify_user_auth_callback)
			_verify_callback_connected = true
		var opts = EOS.Auth.VerifyUserAuthOptions.new()
		opts.auth_token = token
		EOS.Auth.AuthInterface.verify_user_auth(opts)

		# Poczekaj na wynik albo timeout
		var elapsed := 0.0
		while elapsed < timeout_sec:
			await get_tree().process_frame
			if _last_verify_result != null:
				var ok := EOS.is_success(_last_verify_result.result_code)
				if ok:
					_log.info("Poprawnie zweryfikowano token użytkownika po logowaniu")
					login_verified.emit()
					_last_verify_result = null
					return true
				else:
					_log.error("Weryfikacja tokenu nieudana: %s" % EOS.result_str(_last_verify_result))
					login_verification_failed.emit(_last_verify_result.result_code)
					_last_verify_result = null
					return false
			elapsed += get_process_delta_time()
		_log.error("Timeout oczekiwania na verify_user_auth callback")
		login_verification_failed.emit(EOS.Result.TimedOut)
		return false

	# Fallback: sprawdź status logowania jeśli brak sygnału verify
	var status = EOS.Auth.AuthInterface.get_login_status(epic_account_id)
	if status == EOS.LoginStatus.LoggedIn:
		_log.info("Weryfikacja (fallback) OK: status=LoggedIn")
		login_verified.emit()
		return true
	else:
		_log.error("Weryfikacja (fallback) nieudana: status=%s" % status)
		login_verification_failed.emit(EOS.Result.InvalidAuth)
		return false


## Login using credentials provided by the Epic Games Launcher[br]
## To test this locally provide the cli argument -AUTH_PASSWORD=<exchange_code> when running your game like [code]godot4 . -AUTH_PASSWORD=1234[/code][br]
## You can generate an exchange code by using the DevAuthTool and accessing the following link on a browser: [code]http://localhost:<PORT>/<credential_name>/exchange_code[/code]
func login_launcher_async() -> bool:
	_log.debug("Logging in using Epic Games Launcher...")
	var cli_opts = _get_command_line_options()
	var auth_password = cli_opts.get("AUTH_PASSWORD", "")

	if "" == auth_password:
		_log.error("Missing -AUTH_PASSWORD=<exchange_code> cli argument. Please see usage docs.")
		return false

	var opts = EOS.Auth.LoginOptions.new()
	opts.credentials = EOS.Auth.Credentials.new()
	opts.credentials.type = EOS.Auth.LoginCredentialType.ExchangeCode
	opts.credentials.token = auth_password
	opts.scope_flags = auth_login_scope_flags
	opts.login_flags = auth_login_flags

	return await login_async(opts)


## Login using EOS Auth by either using an Identity provider or Epic games Account.
## Allows you to use Epic Account Services: Friends, Presence, Social Overlay, ECom, etc.
## This is the recommended way of logging in as you get many additional features compared to [login_game_services_async]
func login_async(opts: EOS.Auth.LoginOptions) -> bool:
	_log.info("=== login_async wywołany z credentials.type=%s ===" % opts.credentials.type)
	EOS.Auth.AuthInterface.login(opts)

	var auth_login_ret: Dictionary = await IEOS.auth_interface_login_callback
	var auth_res: EOS.Result = auth_login_ret.result_code
	_log.info("Otrzymano odpowiedź z SDK: result_code=%s (%s)" % [auth_res, EOS.result_str(auth_res)])
	_log.debug("Pełna odpowiedź auth_login_ret: %s" % str(auth_login_ret))

	# Handle Device Code / Pin Grant flow: SDK zwraca AuthPinGrantPending dopóki użytkownik nie potwierdzi w przeglądarce
	if auth_res == EOS.Result.AuthPinGrantPending:
		_log.info("AuthPinGrantPending otrzymany - przetwarzam PIN grant...")
		var pin_info = auth_login_ret.get("pin_grant_info", {})
		_log.debug("pin_grant_info dict: %s" % str(pin_info))
		
		var code = ""
		var uri_complete = ""
		var uri_base = ""
		
		if pin_info:
			code = pin_info.get("user_code", "")
			uri_complete = pin_info.get("verification_uri_complete", "")
			uri_base = pin_info.get("verification_uri", "")
		
		_log.info("Kod użytkownika: '%s', URI base: '%s', URI complete: '%s'" % [code, uri_base, uri_complete])
		
		# Jeśli SDK nie zwróciło żadnych danych PIN grant, to jest problem konfiguracji w Dev Portal
		if not code and not uri_base and not uri_complete:
			_log.error("KRYTYCZNY BŁĄD: SDK zwróciło AuthPinGrantPending ale pin_grant_info jest pusty!")
			_log.error("To zazwyczaj oznacza że w Epic Developer Portal:")
			_log.error("1. Aplikacja nie ma włączonego 'Account Portal' w Brand Settings")
			_log.error("2. Client Policy nie ma scope 'basic_profile' lub 'presence'")
			_log.error("3. Niepoprawny Client ID lub Sandbox ID")
			_log.error("Nie mogę kontynuować bez Device Code od SDK - przerywam login")
			_emit_login_auth_error(EOS.Result.AuthUserInterfaceRequired)
			return false
		
		# Jeśli SDK nie zwróciło kompletnego URL z kodem, zbuduj go ręcznie (typowo: verification_uri + ?code=USERCODE)
		var uri = ""
		if uri_complete:
			uri = uri_complete
		else:
			uri = uri_base
			if code != "":
				if uri_base.find("?") == -1:
					uri += "?code=" + code
				else:
					uri += "&code=" + code
		
		_log.info("Finalny URI: '%s'" % uri)
		
		# Jeśli nadal brak URI (bo SDK nie dało ani base ani complete), użyj domyślnego URL Epic
		if not uri:
			uri = "https://www.epicgames.com/id/login/epic?lang=en"
			_log.warn("Brak URI od SDK - używam domyślnego Epic login URL: %s" % uri)
		
		# ZAWSZE emituj sygnał – nawet jeśli kod lub URI są puste, aplikacja powinna wiedzieć o statusie
		_last_pin_code = code
		pin_grant_code_current = code
		pin_grant_verification_uri_current = uri
		_log.info("Emituję sygnał pin_grant_updated z kodem='%s' i uri='%s'" % [code, uri])
		emit_signal("pin_grant_updated", code, uri)
		
		# Otwórz przeglądarkę tylko raz – SDK przy kolejnych wywołaniach login() potrafi samo odpalać UI, co kończy się wieloma kartami.
		if uri and not _pin_grant_browser_opened:
			_log.info("PIN grant pending (pierwsze wywołanie). Otwieram przeglądarkę: %s" % uri)
			OS.shell_open(uri)
			_pin_grant_browser_opened = true
		elif not uri:
			_log.warn("Brak URI weryfikacji – nie mogę otworzyć przeglądarki")
		
		if code:
			_log.info("Wprowadź kod: %s jeśli zostaniesz o to poproszony." % code)
		else:
			_log.warn("SDK nie zwróciło kodu użytkownika w pin_grant_info")
		
		# Pollujemy przez ograniczony czas aż użytkownik zaakceptuje (albo przerwie)
		# Od teraz trzymaj UI w trybie NoUserInterface aby SDK nie otwierało nic automatycznie w kolejnych próbach.
		opts.login_flags = EOS.Auth.LoginFlags.NoUserInterface
		var max_attempts := 60 # ~120s przy interwale 2s
		while max_attempts > 0:
			await get_tree().create_timer(2.0).timeout
			# Przy ponownych próbach ogranicz interfejs aby SDK nie odpalało nowych kart – jeśli flaga istnieje ekosystem ją zignoruje, ale nie zaszkodzi.
			# (Nie wszystkie buildy EOS SDK respektują login_flags w retry, ale próbujemy zminimalizować UI spam.)
			EOS.Auth.AuthInterface.login(opts)
			auth_login_ret = await IEOS.auth_interface_login_callback
			auth_res = auth_login_ret.result_code
			# Jeżeli SDK poda nowy kod w trakcie czekania, odśwież go w UI (bez ponownego otwierania okna)
			if auth_res == EOS.Result.AuthPinGrantPending:
				var retry_pin = auth_login_ret.get("pin_grant_info", {})
				if retry_pin:
					var new_code = retry_pin.get("user_code", "")
					var new_uri_complete = retry_pin.get("verification_uri_complete", "")
					var new_uri_base = retry_pin.get("verification_uri", "")
					var new_uri = ""
					if new_uri_complete:
						new_uri = new_uri_complete
					else:
						new_uri = new_uri_base
						if new_code != "":
							if new_uri_base.find("?") == -1:
								new_uri += "?code=" + new_code
							else:
								new_uri += "&code=" + new_code
					if new_code and new_code != _last_pin_code:
						_log.info("Nowy kod PIN w retry: '%s', emituję aktualizację" % new_code)
						_last_pin_code = new_code
						pin_grant_code_current = new_code
						pin_grant_verification_uri_current = new_uri
						emit_signal("pin_grant_updated", new_code, new_uri)
			# Nie otwieraj przeglądarki ponownie nawet jeśli SDK zwróci nowe pin_info – użytkownik już ma kartę.
			if auth_res != EOS.Result.AuthPinGrantPending:
				break
			max_attempts -= 1

	if auth_res == EOS.Result.AuthMFARequired:
		_log.error("Auth requires MFA - This is not supported by the EOS SDK. Please use an account without MFA or use an alternative login method")
		_emit_login_auth_error(auth_res)
		return false
	
	if auth_res == EOS.Result.InvalidUser:
		if not auth_login_ret.continuance_token:
			_log.error("Auth login failed - Continuance token is invalid")
			_emit_login_auth_error(EOS.Result.InvalidState)
			return false
		
		if not auto_link_account:
			_log.error("Auth login failed - External account not connected")
			_emit_login_auth_error(EOS.Result.InvalidUser)
			return false
		
		_log.debug("External account not found. Proceeding to connect account...")
		var continue_success = await _continue_login_async(auth_login_ret.continuance_token)
		if not continue_success:
			return false
	
	elif not EOS.is_success(auth_res):
		# Provide more context when PersistentAuth fails so user knows typical causes
		if auth_res == EOS.Result.InvalidAuth and opts and opts.credentials and opts.credentials.type == EOS.Auth.LoginCredentialType.PersistentAuth:
			_log.error("Failed PersistentAuth login (InvalidAuth). Najczęstsze przyczyny: brak zapisanego refresh tokenu, token wygasł, zmiana client_id / sandbox albo nie było wcześniejszego loginu przez AccountPortal. Wykonaj najpierw login_account_portal_async, zrestartuj grę i spróbuj ponownie.")
		else:
			_log.error("Failed to login with EOS Auth: result_code=%s" % EOS.result_str(auth_res))
		_emit_login_auth_error(auth_res)
		return false
	
	if auth_login_ret.selected_account_id:
		epic_account_id = auth_login_ret.selected_account_id
	
	if epic_account_id:
		_log.info("Logged into Epic Account Services with Epic Account ID: %s" % epic_account_id)
	
	if not auto_connect_account:
		logged_in_auth.emit()
		logged_in.emit()
		# Opcjonalnie automatyczna weryfikacja (nie blokuj logowania jeśli się nie uda)
		call_deferred("_auto_verify_post_login")
		return true

	return await _connect_account_async()


func _auto_verify_post_login():
	# Używamy krótszego timeoutu aby nie blokować
	verify_browser_login_async(5.0)


## Logout from EOS Auth and or EOS Connect
func logout_async() -> EOS.Result:
	_log.verbose("Logging out from EOS...")
	var ret := EOS.Result.InvalidAuth
	var _logged_out = false

	if product_user_id:
		_log.debug("Logging out from EOS Connect")
		var logout_connect_opts = EOS.Connect.LogoutOptions.new()
		EOS.Connect.ConnectInterface.logout(logout_connect_opts)
		var logout_connect_ret = await IEOS.connect_interface_logout_callback
		ret = logout_connect_ret.result_code

		if not EOS.is_success(ret):
			_log.error("Failed to logout of EOS Connect. result_code=%s" % EOS.result_str(ret))
			return ret
		else:
			_log.debug("Logged out from EOS connect: product_user_id=%s" % product_user_id)
			product_user_id = ""
			_logged_out = true
			logged_out_connect.emit()


	if epic_account_id:
		_log.debug("Logging out from EOS Auth")
		var logout_auth_opts = EOS.Auth.LogoutOptions.new()
		EOS.Auth.AuthInterface.logout(logout_auth_opts)
		var logout_auth_ret = await IEOS.auth_interface_logout_callback
		ret = logout_auth_ret.result_code
	
		if not EOS.is_success(ret):
			_log.error("Failed to logout of EOS Auth. result_code=%s" % EOS.result_str(ret))
		else:
			_log.debug("Logged out from EOS Auth: epic_account_id=%s" % epic_account_id)
			epic_account_id = ""
			_logged_out = true
			logged_out_auth.emit()


	if _logged_out:
		display_name = ""
		external_account_info = {}
		display_name_changed.emit()
		external_account_info_changed.emit()
		logged_out.emit()

	return ret
	

## Login with EOS Connect by using external credentials
func login_game_services_async(opts: EOS.Connect.LoginOptions) -> bool:
	_log.debug("Logging into Epic Game Services (ConnectInterface)...")
	EOS.Connect.ConnectInterface.login(opts)

	var login_ret: Dictionary = await IEOS.connect_interface_login_callback
	var login_res: EOS.Result = login_ret.result_code
	
	if login_res == EOS.Result.InvalidUser:
		_log.debug("Epic Game Services user not found. Proceeding to create user...")
		var create_success := await _create_user_async(login_ret.continuance_token)
		if not create_success:
			return false
	
	elif not EOS.is_success(login_ret):
		_log.error("Failed to login to Epic Game Services: result_code=%s" % EOS.result_str(login_res))
		_emit_login_connect_error(login_res)
		return false

	if login_ret.local_user_id:
		product_user_id = login_ret.local_user_id
	
	if product_user_id:
		_log.info("Logged into Epic Games Services with Product User Id: %s" % product_user_id)
		if auto_fetch_external_account and EOS.ExternalCredentialType.DeviceidAccessToken != opts.credentials.type:
			get_product_user_info_async()

	logged_in_connect.emit()
	logged_in.emit()
	return true


## Login automatically if an Epic refresh token is stored by a previous login with Epic Account Portal
func login_persistent_auth_async() -> bool:
	# If we're already logged in to Auth, PersistentAuth is not needed and calling it may yield InvalidAuth
	if epic_account_id:
		_log.debug("Already logged into EOS Auth; skipping PersistentAuth login request")
		return true

	_log.debug("Logging in with persistent auth...")
	var opts = EOS.Auth.LoginOptions.new()
	opts.credentials = EOS.Auth.Credentials.new()
	opts.credentials.type = EOS.Auth.LoginCredentialType.PersistentAuth
	opts.scope_flags = auth_login_scope_flags
	opts.login_flags = auth_login_flags

	return await login_async(opts)


## Delete the internally stored Epic refresh token
func delete_persistent_auth_async(refresh_token := "") -> bool:
	_log.info("=== ROZPOCZYNAM USUWANIE PERSISTENT AUTH ===")
	_log.debug("refresh_token parametr: '%s' (długość: %d)" % [refresh_token, len(refresh_token)])
	
	# Jeśli nie podano tokenu, spróbuj pobrać z obecnie zalogowanego użytkownika
	if refresh_token == "" and epic_account_id:
		_log.debug("Próba pobrania tokenu z obecnie zalogowanego użytkownika: %s" % epic_account_id)
		var token_ret = EOS.Auth.AuthInterface.copy_user_auth_token(EOS.Auth.CopyUserAuthTokenOptions.new(), epic_account_id)
		if EOS.is_success(token_ret) and token_ret.token:
			refresh_token = token_ret.token.refresh_token if token_ret.token.refresh_token else ""
			_log.debug("Pobrano refresh_token z zalogowanego użytkownika: '%s' (długość: %d)" % [refresh_token, len(refresh_token)])
		else:
			_log.warn("Nie można pobrać tokenu z zalogowanego użytkownika: %s" % EOS.result_str(token_ret.result_code if typeof(token_ret) == TYPE_DICTIONARY and "result_code" in token_ret else EOS.Result.InvalidState))
	
	var opts = EOS.Auth.DeletePersistentAuthOptions.new()
	# Zgodnie z dokumentacją EOS: jeśli refresh_token jest null/empty, usuwa WSZYSTKIE tokeny dla tej aplikacji
	# Jednak niektóre wersje SDK wymagają explicit null zamiast pustego stringa
	if refresh_token == "":
		opts.refresh_token = null
		_log.info("Usuwam WSZYSTKIE persistent auth tokeny (refresh_token=null)")
	else:
		opts.refresh_token = refresh_token
		_log.info("Usuwam konkretny refresh token (długość: %d)" % len(refresh_token))
	
	_log.debug("Wywołuję AuthInterface.delete_persistent_auth...")
	EOS.Auth.AuthInterface.delete_persistent_auth(opts)

	var ret = await IEOS.auth_interface_delete_persistent_auth_callback
	_log.debug("Otrzymano odpowiedź: %s" % str(ret))
	
	if not EOS.is_success(ret):
		_log.error("Failed to delete persistent auth: result_code=%s (%s)" % [ret, EOS.result_str(ret)])
		_log.error("Możliwe przyczyny:")
		_log.error("  1. Niepoprawny refresh_token")
		_log.error("  2. Brak zapisanych tokenów do usunięcia")
		_log.error("  3. SDK nie został poprawnie zainicjalizowany")
		_log.error("Spróbuj ręcznie usunąć folder: user://eosg-cache/")
		return false
	
	_log.info("✓ Persistent auth pomyślnie usunięty!")
	return true


## Ręczne usunięcie folderu cache EOS (zawiera persistent auth tokeny)
## Użyj tego jako fallback jeśli delete_persistent_auth_async() zwraca błąd
func delete_cache_directory_manual() -> bool:
	var cache_path = "user://eosg-cache"
	var global_path = ProjectSettings.globalize_path(cache_path)
	
	_log.info("=== RĘCZNE USUWANIE CACHE EOS ===")
	_log.info("Ścieżka: %s" % global_path)
	
	var dir = DirAccess.open("user://")
	if not dir:
		_log.error("Nie można otworzyć katalogu user://")
		return false
	
	if not dir.dir_exists("eosg-cache"):
		_log.warn("Folder eosg-cache nie istnieje - nic do usunięcia")
		return true
	
	# Usuń rekursywnie cały folder
	var err = _remove_directory_recursive(cache_path)
	if err != OK:
		_log.error("Nie udało się usunąć folderu cache: error=%d" % err)
		_log.error("Spróbuj ręcznie usunąć folder: %s" % global_path)
		return false
	
	_log.info("✓ Folder cache został pomyślnie usunięty!")
	_log.info("Zrestartuj aplikację aby zmiany zostały w pełni zastosowane")
	return true


# Rekursywne usuwanie katalogu
func _remove_directory_recursive(path: String) -> Error:
	var dir = DirAccess.open(path)
	if not dir:
		return DirAccess.get_open_error()
	
	dir.list_dir_begin()
	var file_name = dir.get_next()
	while file_name != "":
		var full_path = path + "/" + file_name
		if dir.current_is_dir():
			if file_name != "." and file_name != "..":
				var err = _remove_directory_recursive(full_path)
				if err != OK:
					return err
		else:
			var err = dir.remove(file_name)
			if err != OK:
				_log.error("Nie można usunąć pliku: %s (error=%d)" % [full_path, err])
				return err
		file_name = dir.get_next()
	dir.list_dir_end()
	
	# Usuń sam katalog
	return DirAccess.remove_absolute(ProjectSettings.globalize_path(path))


## Login to Epic Game Services without any credentials.
## You must provide a user display name.
func login_anonymous_async(p_user_display_name: String) -> bool:
	var user_display_name := p_user_display_name.strip_edges()
	if not p_user_display_name:
		_log.error("User display name is empty")
		return false
	_log.debug("Logging in anonymously...")

	EOS.Connect.ConnectInterface.delete_device_id(EOS.Connect.DeleteDeviceIdOptions.new())
	var delete_ret = await IEOS.connect_interface_delete_device_id_callback
	if not EOS.is_success(delete_ret):
		_log.debug("Failed to delete device id: result_code=%s" % EOS.result_str(delete_ret))
	
	var opts = EOS.Connect.CreateDeviceIdOptions.new()
	opts.device_model = " ".join(PackedStringArray([OS.get_name(), OS.get_model_name()]))
	EOS.Connect.ConnectInterface.create_device_id(opts)

	var create_ret = await IEOS.connect_interface_create_device_id_callback
	if not EOS.is_success(create_ret):
		_log.error("Failed to create device id: result_code=%s" % EOS.result_str(create_ret))
		return false
	
	var login_opts = EOS.Connect.LoginOptions.new()
	login_opts.credentials = EOS.Connect.Credentials.new()
	login_opts.credentials.type = EOS.ExternalCredentialType.DeviceidAccessToken
	login_opts.credentials.token = null
	login_opts.user_login_info = EOS.Connect.UserLoginInfo.new()
	login_opts.user_login_info.display_name = user_display_name
	display_name = user_display_name
	display_name_changed.emit()
	
	return await login_game_services_async(login_opts)


## Get the user info from epic account id.[br]
## Returns a [Dictionary] with the following keys or empty dictionary if error occurred:[codeblock]
## user_id: String
## country: String
## display_name: String
## display_name_sanitized: String
## preferred_language: String
## nickname: String
## [/codeblock]
func get_user_info_async(p_epic_account_id := epic_account_id) -> Dictionary:
	_log.verbose("Querying user info: epic_account_id=%s" % p_epic_account_id)
	var query_opts = EOS.UserInfo.QueryUserInfoOptions.new()
	query_opts.local_user_id = epic_account_id
	query_opts.target_user_id = p_epic_account_id
	EOS.UserInfo.UserInfoInterface.query_user_info(query_opts)

	var ret: Dictionary = await IEOS.user_info_interface_query_user_info_callback
	if not EOS.is_success(ret):
		_log.error("Failed to query user info: result_code=%s" % EOS.result_str(ret))
		return {}
	
	var copy_opts = EOS.UserInfo.CopyUserInfoOptions.new()
	copy_opts.local_user_id = epic_account_id
	copy_opts.target_user_id = p_epic_account_id
	var copy_ret = EOS.UserInfo.UserInfoInterface.copy_user_info(copy_opts)
	if not EOS.is_success(copy_ret):
		_log.error("Failed to copy user info: result_code=%s" % EOS.result_str(copy_ret))
		return {}

	return copy_ret.user_info


## Get the external user account linked with Epic Game Services[br]
## Returns a [Dictionary] with the following keys or empty dictionary if error occurred:
## [codeblock]
## product_user_id: String - the product user ID of the external account
## display_name: String - external account display name or empty string
## account_id: String - external account id
## account_id_type: EOS.ExternalAccountType - type of external account
## last_login_time: int - unix timestamp when the user last logged in or -1
## [/codeblock]
func get_external_account_by_type_async(p_external_account_type: EOS.ExternalAccountType, p_product_user_id := product_user_id) -> Dictionary:
	_log.debug("Getting external account by type: external_account_type=%s product_user_id=%s" % [p_external_account_type, p_product_user_id])

	var opts = EOS.Connect.QueryProductUserIdMappingsOptions.new()
	opts.product_user_ids = [p_product_user_id]
	EOS.Connect.ConnectInterface.query_product_user_id_mappings(opts)
	var ret = await IEOS.connect_interface_query_product_user_id_mappings_callback
	if not EOS.is_success(ret):
		_log.error("Failed to query product user id mappings: result_code=%s" % EOS.result_str(ret))
		return {}

	var copy_opts = EOS.Connect.CopyProductUserExternalAccountByAccountTypeOptions.new()
	copy_opts.account_id_type = p_external_account_type
	copy_opts.target_user_id = p_product_user_id
	var copy_ret = EOS.Connect.ConnectInterface.copy_product_user_external_account_by_account_type(copy_opts)
	if not EOS.is_success(copy_ret):
		_log.error("Failed to copy external account: result_code=%s" % EOS.result_str(copy_ret))
		return {}
	
	_log.debug("Got external account info: product_user_id=%s" % p_product_user_id)
	
	var acc_info = copy_ret.external_account_info
	if not acc_info:
		acc_info = {}
		_log.error("Failed to get external account info")
	
	return acc_info


## Get all external accounts linked with Epic Games Services[br]
## Returns a [Dictionary] with same keys as [method get_external_account_by_type_async][br]
func get_external_accounts_async(p_product_user_id := product_user_id) -> Array:
	_log.debug("Getting all external accounts: product_user_id=%s" % p_product_user_id)

	var opts = EOS.Connect.QueryProductUserIdMappingsOptions.new()
	opts.product_user_ids = [p_product_user_id]
	EOS.Connect.ConnectInterface.query_product_user_id_mappings(opts)
	var ret = await IEOS.connect_interface_query_product_user_id_mappings_callback
	if not EOS.is_success(ret):
		_log.error("Failed to query product user id mappings: result_code=%s" % EOS.result_str(ret))
		return []


	var count_opts = EOS.Connect.GetProductUserExternalAccountCountOptions.new()
	count_opts.target_user_id = p_product_user_id
	var count = EOS.Connect.ConnectInterface.get_product_user_external_account_count(count_opts)

	var ext_accs = []
	for i in range(count):
		var copy_opts = EOS.Connect.CopyProductUserExternalAccountByIndexOptions.new()
		copy_opts.target_user_id = p_product_user_id
		copy_opts.external_account_info_index = i

		var copy_ret = EOS.Connect.ConnectInterface.copy_product_user_external_account_by_index(copy_opts)
		if not EOS.is_success(copy_ret):
			_log.error("Failed to copy external account: result_code=%s, index=%s" % [EOS.result_str(copy_ret), i])
			continue
		if copy_ret.external_account_info:
			ext_accs.append(copy_ret.external_account_info)

	return ext_accs


## Get the external account linked with Epic Game Services that the user most recently logged in with.[br]
## Returns a [Dictionary] with same keys as [method get_external_account_by_type_async]
func get_product_user_info_async(p_product_user_id := product_user_id):
	_log.debug("Getting product user info: product_user_id=%s" % p_product_user_id)

	var opts = EOS.Connect.QueryProductUserIdMappingsOptions.new()
	opts.product_user_ids = [p_product_user_id]
	EOS.Connect.ConnectInterface.query_product_user_id_mappings(opts)
	var ret = await IEOS.connect_interface_query_product_user_id_mappings_callback
	if not EOS.is_success(ret):
		_log.error("Failed to query product user id mappings: result_code=%s" % EOS.result_str(ret))
		return {}

	var copy_opts = EOS.Connect.CopyProductUserInfoOptions.new()
	copy_opts.target_user_id = p_product_user_id
	var copy_ret = EOS.Connect.ConnectInterface.copy_product_user_info(copy_opts)
	if not EOS.is_success(copy_ret):
		_log.error("Failed to copy product user info: result_code=%s" % EOS.result_str(copy_ret))
		return {}
	
	_log.debug("Got product user info: product_user_id=%s" % p_product_user_id)

	var ext_acc = copy_ret.external_account_info
	if not ext_acc:
		_log.error("Failed to get external account info")
	if ext_acc and ext_acc.product_user_id == product_user_id:
		external_account_info = ext_acc
		external_account_info_changed.emit()

		if ext_acc.display_name:
			display_name = ext_acc.display_name
			display_name_changed.emit()

	return ext_acc

#endregion


#region Private methods

func _on_connect_interface_auth_expiration(data: Dictionary):
	_log.debug("Connect Auth Expiring...")
	_connect_account_async()


func _connect_account_async() -> bool:
	_log.debug("Connecting account to Epic Game Services...")

	# Copy the user auth token
	var auth_token_ret = EOS.Auth.AuthInterface.copy_user_auth_token(EOS.Auth.CopyUserAuthTokenOptions.new(), epic_account_id)
	if not EOS.is_success(auth_token_ret):
		_log.error("Failed to get user auth token: result_code=%s" % EOS.result_str(auth_token_ret))
		_emit_login_auth_error(auth_token_ret.result_code)
		return false

	var token = auth_token_ret.token

	var login_options = EOS.Connect.LoginOptions.new()
	login_options.credentials = EOS.Connect.Credentials.new()
	login_options.credentials.type = EOS.ExternalCredentialType.Epic
	login_options.credentials.token = token.access_token

	# Emit signal only if we are not refreshing the login
	if not epic_account_id:
		logged_in_auth.emit()

	return await login_game_services_async(login_options)


var _last_verify_result = null
func _on_verify_user_auth_callback(data: Dictionary):
	_last_verify_result = data


func _continue_login_async(continuance_token: EOSGContinuanceToken) -> bool:
	_log.debug("Continuing login...")
	var link_opts = EOS.Auth.LinkAccountOptions.new()
	link_opts.continuance_token = continuance_token
	EOS.Auth.AuthInterface.link_account(link_opts)

	var link_ret: Dictionary = await IEOS.auth_interface_link_account_callback
	if not EOS.is_success(link_ret):
		_log.error("Failed to link account: result_code=%s" % EOS.result_str(link_ret))
		_emit_login_auth_error(link_ret.result_code)
		return false
	
	if link_ret.selected_account_id:
		epic_account_id = link_ret.selected_account_id

	return true
	

func _create_user_async(continuance_token: EOSGContinuanceToken) -> bool:
	_log.debug("Creating user...")
	var create_opts = EOS.Connect.CreateUserOptions.new()
	create_opts.continuance_token = continuance_token
	EOS.Connect.ConnectInterface.create_user(create_opts)

	var create_ret: Dictionary = await IEOS.connect_interface_create_user_callback

	if not EOS.is_success(create_ret):
		_log.error("Failed to create user: result_code=%s" % EOS.result_str(create_ret))
		_emit_login_connect_error(create_ret.result_code)
		return false
	
	if create_ret.local_user_id:
		product_user_id = create_ret.local_user_id
	
	return true


func _emit_login_auth_error(result_code: EOS.Result):
	login_auth_error.emit(result_code)
	login_error.emit(result_code)


func _emit_login_connect_error(result_code: EOS.Result):
	login_connect_error.emit(result_code)
	login_error.emit(result_code)


func _get_command_line_options():
	var options = {}
	var args = OS.get_cmdline_args()
	for arg in args:
		arg = arg.trim_prefix("--").trim_prefix("-")
		var kvp = arg.split("=")
		if len(kvp) > 1:
			options[kvp[0]] = kvp[1]
	return options

#endregion
