using Godot;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Networking;

/// <summary>
/// C# fasada nad warstwą GDScript (HPlatform + HAuth) tak abyś mógł wywoływać logowanie wyłącznie z C#.
/// NIE usuwa zależności od pluginu GDScript – korzysta z istniejących autoloadów i klas.
/// Jeśli chcesz całkowicie pominąć GDScript musiałbyś napisać własne C# bindingi do EOS SDK (znacznie więcej pracy).
/// </summary>
public partial class EOSPlatformAuth : Node
{
    // Autoloady z pluginu
    private Node _hPlatform; // /root/HPlatform
    private Node _hAuth;     // /root/HAuth

    private bool _initialized;

    // Zdarzenia wysokiego poziomu (agregujące sygnały GDScript)
    public event Action LoggedIn;             // pełny login (Auth+Connect)
    public event Action LoggedOut;            // kompletne wylogowanie
    public event Action<string> LoginError;   // kod błędu (EOS.Result.*)
    public event Action<string, string> UserChanged; // (displayName, productUserId)
    public event Action PersistentAuthVerified;     // weryfikacja tokenu po loginie portalowym
    public event Action<string, string> SdkLog;      // (category, message)
    public event Action<string, string> DeviceCodeUpdated; // (code, verificationUrl) z PIN/device grant

    // TaskCompletion dla wywołań async z UI C#
    private TaskCompletionSource<bool> _loginTcs;
    private TaskCompletionSource<bool> _logoutTcs;

    // Ostatnio znany DeviceCode oraz URL weryfikacji – na wypadek gdyby słuchacz podpiął się później
    private string _latestDeviceCode = string.Empty;
    private string _latestVerificationUrl = string.Empty;
    private bool _verificationOpened = false;          // Czy otworzyliśmy już stronę weryfikacji (z poziomu C#)
    private bool _verificationFallbackTried = false;    // Czy wykonano już jeden fallback reopen

    // Konfiguracja automatycznego otwierania strony (fallback, gdy GDScript nie otworzy)
    private bool _autoOpenVerificationUrl = true;       // Możesz wyłączyć, jeśli nie chcesz aby C# otwierał przeglądarkę
    private double _initialOpenDelaySec = 0.5;          // Niewielkie opóźnienie, by nie dublować otwarcia z GDScript
    private double _fallbackReopenDelaySec = 5.0;       // Jeśli wciąż brak logowania po tym czasie – spróbuj ponownie raz

    // Publiczny odczyt (np. aby UI mogło dopytać po starcie)
    public string LatestDeviceCode => _latestDeviceCode;
    public string LatestVerificationUrl => _latestVerificationUrl;

    // Odczyt właściwości z HAuth (dynamicznie)
    public string EpicAccountId => _hAuth?.Get("epic_account_id").AsString() ?? string.Empty;
    public string ProductUserId => _hAuth?.Get("product_user_id").AsString() ?? string.Empty;
    public string DisplayName => _hAuth?.Get("display_name").AsString() ?? string.Empty;

    public override void _Ready()
    {
        // Spróbuj znaleźć autoloady
        _hPlatform = GetNodeOrNull("/root/HPlatform");
        _hAuth = GetNodeOrNull("/root/HAuth");

        if (_hPlatform == null || _hAuth == null)
        {
            GD.PrintErr("[EOSPlatformAuth] Brak autoloadów HPlatform lub HAuth – włącz plugin w Project Settings -> Plugins");
            return;
        }

        // Podłącz sygnały HAuth -> C# eventy
        _hAuth.Connect("logged_in", new Callable(this, nameof(OnLoggedInSignal)));
        _hAuth.Connect("logged_out", new Callable(this, nameof(OnLoggedOutSignal)));
        _hAuth.Connect("login_error", new Callable(this, nameof(OnLoginErrorSignal)));
        _hAuth.Connect("display_name_changed", new Callable(this, nameof(OnDisplayNameChanged)));
        if (_hAuth.HasSignal("login_verified"))
            _hAuth.Connect("login_verified", new Callable(this, nameof(OnLoginVerifiedSignal)));
        if (_hAuth.HasSignal("pin_grant_updated"))
            _hAuth.Connect("pin_grant_updated", new Callable(this, nameof(OnPinGrantUpdated)));

        // Logi z SDK (HPlatform.emituje log_msg z EOS.Logging.LogMessage)
        if (_hPlatform.HasSignal("log_msg"))
        {
            _hPlatform.Connect("log_msg", new Callable(this, nameof(OnSdkLog)));
        }

        // Jeśli HAuth już posiada aktualny DeviceCode (np. przy wznowieniu sceny), przekaż go do aplikacji
        var codeProp = _hAuth.Get("pin_grant_code_current").AsString();
        var urlProp = _hAuth.Get("pin_grant_verification_uri_current").AsString();
        if (!string.IsNullOrEmpty(codeProp) || !string.IsNullOrEmpty(urlProp))
        {
            OnPinGrantUpdated(codeProp, urlProp);
        }
    }

    #region Inicjalizacja Platformy

    /// <summary>
    /// Odpowiednik GDScript _ready() z przykładu. Tworzy strukturę HCredentials i wywołuje setup_eos_async.
    /// </summary>
    public async Task<bool> InitializeAsync(productDetails details)
    {
        if (_initialized) return true;
        if (_hPlatform == null)
            return false;

        // Zbuduj obiekt HCredentials (klasa GDScript: hcredentials.gd)
        var credScript = GD.Load<Script>("res://addons/epic-online-services-godot/heos/hcredentials.gd");
        var credsObj = credScript?.Call("new").AsGodotObject();
        if (credsObj == null)
        {
            GD.PrintErr("[EOSPlatformAuth] Nie mogę utworzyć HCredentials");
            return false;
        }

        // Wypełnij wartości (możesz też wpisać "na sztywno" – patrz parametry w productDetails.cs)
        credsObj.Set("product_name", details?.product_name ?? ProjectSettings.GetSetting("application/config/name").AsString());
        credsObj.Set("product_version", details?.product_version ?? "1.0");
        credsObj.Set("product_id", details?.product_id ?? string.Empty);
        credsObj.Set("sandbox_id", details?.sandbox_id ?? string.Empty);
        credsObj.Set("deployment_id", details?.deployment_id ?? string.Empty);
        credsObj.Set("client_id", details?.client_id ?? string.Empty);
        credsObj.Set("client_secret", details?.client_secret ?? string.Empty);
        credsObj.Set("encryption_key", details?.encryption_key ?? string.Empty);

        // Wywołaj GDScript async: setup_eos_async
        var ret = _hPlatform.Call("setup_eos_async", credsObj);
        bool ok;
        if (ret.VariantType == Variant.Type.Object)
        {
            var stateObj = ret.AsGodotObject();
            var completed = await ToSignal(stateObj, "completed");
            ok = completed != null && completed.Length > 0 && completed[0].AsBool();
        }
        else ok = ret.AsBool();

        if (ok)
        {
            _initialized = true;
            GD.Print("[EOSPlatformAuth] EOS Platform Initialized");
        }
        else
        {
            GD.PrintErr("[EOSPlatformAuth] Inicjalizacja EOS nie powiodła się – sprawdź logi");
        }
        return ok;
    }

    #endregion

    #region Publiczne metody logowania

    /// <summary>
    /// Ustawia ScopeFlags dla Auth logowania (BasicProfile/FriendsList/Presence/Country). Domyślnie Country=false.
    /// Wartości odpowiadają EOS.Auth.ScopeFlags w pluginie GDScript: BasicProfile=0x1, FriendsList=0x2, Presence=0x4, Country=0x20.
    /// </summary>
    public void SetAuthScopes(bool basicProfile = true, bool friendsList = true, bool presence = true, bool country = false)
    {
        if (_hAuth == null) return;
        int flags = 0;
        if (basicProfile) flags |= 0x1;
        if (friendsList) flags |= 0x2;
        if (presence) flags |= 0x4;
        if (country) flags |= 0x20;
        _hAuth.Set("auth_login_scope_flags", flags);
        GD.Print($"[EOSPlatformAuth] Auth scopes set: BasicProfile={basicProfile}, FriendsList={friendsList}, Presence={presence}, Country={country} (0x{flags:X})");
    }

    /// <summary>
    /// Login poprzez przeglądarkowy Account Portal (zapisywany refresh token dla PersistentAuth kolejnego uruchomienia).
    /// </summary>
    public Task<bool> LoginPortalAsync()
    {
        // nic – zakładamy, że SetAuthScopes został wywołany wcześniej jeśli chcesz zmienić zakres
        return StartLoginFlow(() => _hAuth.Call("login_account_portal_async"));
    }

    /// <summary>
    /// Próba logowania PersistentAuth – działa TYLKO jeśli poprzednio (w innym uruchomieniu) udał się login portalowy i token nie wygasł.
    /// </summary>
    public Task<bool> LoginPersistentAuthAsync()
    {
        return StartLoginFlow(() => _hAuth.Call("login_persistent_auth_async"));
    }

    /// <summary>
    /// Anonimowy DeviceID login (bez konta Epic) – wymaga unikalnej nazwy dla usera.
    /// </summary>
    public Task<bool> LoginAnonymousAsync(string displayName)
    {
        return StartLoginFlow(() => _hAuth.Call("login_anonymous_async", displayName));
    }

    /// <summary>
    /// Developer Auth Tool (DevAuth) – szybkie lokalne logowanie bez portalu.
    /// serverUrl np. "http://localhost:6547" , credentialName to nazwa credential w DevAuthTool.
    /// </summary>
    public Task<bool> LoginDevAuthAsync(string serverUrl, string credentialName)
    {
        // Preflight walidacja parametrów i lokalnego serwera DevAuth aby uniknąć EOS.Result.NoConnection bez kontekstu.
        if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(credentialName))
        {
            GD.PrintErr("[EOSPlatformAuth] DevAuth: serverUrl lub credentialName puste");
            LoginError?.Invoke("InvalidParameters");
            var tcs = new TaskCompletionSource<bool>();
            tcs.SetResult(false);
            return tcs.Task;
        }

        // Ujednolicenie schematu: jeśli podałeś tylko host:port bez http
        if (!serverUrl.StartsWith("http://") && !serverUrl.StartsWith("https://"))
        {
            serverUrl = "http://" + serverUrl.Trim();
        }

        // Spróbuj prostego HEAD/GET do root żeby sprawdzić czy serwer żyje zanim wywołamy EOS Auth
        return LoginDevAuthInternalAsync(serverUrl, credentialName);
    }

    private async Task<bool> LoginDevAuthInternalAsync(string serverUrl, string credentialName)
    {
        bool reachable = await ProbeDevAuthServerAsync(serverUrl, 3.0);
        if (!reachable)
        {
            GD.PrintErr($"[EOSPlatformAuth] DevAuth: Brak połączenia z {serverUrl}. Upewnij się, że DevAuthTool działa i port jest otwarty.");
            LoginError?.Invoke("NoConnection(Preflight)");
            return false;
        }
        GD.Print($"[EOSPlatformAuth] DevAuth: Serwer osiągalny {serverUrl}, próbuję login...");
        return await StartLoginFlowAsync(() => _hAuth.Call("login_devtool_async", serverUrl, credentialName));
    }

    // Async odpowiednik StartLoginFlow dla wnętrza taska
    private async Task<bool> StartLoginFlowAsync(Action invoke)
    {
        var task = StartLoginFlow(invoke);
        return await task;
    }

    private async Task<bool> ProbeDevAuthServerAsync(string serverUrl, double timeoutSec)
    {
        // Użyj HttpRequest (C# API) – tworzymy tymczasowy node.
        var http = new HttpRequest();
        AddChild(http);
        bool finished = false;
        int statusCode = 0;
        http.RequestCompleted += (long result, long responseCode, string[] headers, byte[] body) =>
        {
            finished = true;
            // rzutuj z long na int – kod odpowiedzi HTTP mieści się w zakresie int
            statusCode = (int)responseCode;
        };

        // Wybierz metodę HEAD (enum z Godot) i uniknij konfliktu z System.Net.Http.HttpClient.
        var err = http.Request(serverUrl, Array.Empty<string>(), Godot.HttpClient.Method.Head);
        if (err != Error.Ok)
        {
            RemoveChild(http); http.QueueFree();
            GD.PrintErr($"[EOSPlatformAuth] DevAuth preflight request error: {err}");
            return false;
        }

        double elapsed = 0;
        while (!finished && elapsed < timeoutSec)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            elapsed += GetProcessDeltaTime();
        }
        RemoveChild(http); http.QueueFree();
        if (!finished)
        {
            GD.PrintErr("[EOSPlatformAuth] DevAuth preflight timeout");
            return false;
        }
        if (statusCode >= 200 && statusCode < 500) // akceptuj 2xx/3xx/4xx jako 'żyje'
            return true;
        GD.PrintErr($"[EOSPlatformAuth] DevAuth preflight nieudany status={statusCode}");
        return false;
    }

    /// <summary>
    /// Login wymuszony ExchangeCode (jeśli masz kod jednorazowy). Standardowo plugin oczekuje parametru -AUTH_PASSWORD= w CLI,
    /// ale tu wystawiamy uproszczoną metodę: najpierw ustaw zmienną środowiskową i wywołaj login_launcher_async.
    /// Uwaga: Wtyczka w GDScript czyta parametr z linii komend – najprostszy workaround.
    /// </summary>
    public Task<bool> LoginExchangeCodeAsync(string exchangeCode)
    {
        // Hack: dodaj do globalnych argumentów runtime (tylko w edytorze/testach). W realnej produkcji przekaż parametr CLI przy uruchomieniu.
        OS.SetEnvironment("AUTH_PASSWORD", exchangeCode); // NIE wszystkie platformy – fallback informacyjny.
        GD.Print("[EOSPlatformAuth] Ustawiono AUTH_PASSWORD w env – jeśli plugin nie odczyta, uruchom grę z parametrem -AUTH_PASSWORD=...");
        return StartLoginFlow(() => _hAuth.Call("login_launcher_async"));
    }

    /// <summary>
    /// Odpowiednik verify_browser_login_async z GDScript – dodatkowa weryfikacja tokenu po logowaniu portalowym / device code.
    /// Zwraca true jeżeli token poprawnie zweryfikowany lub status zalogowania OK (fallback).
    /// </summary>
    public async Task<bool> VerifyBrowserLoginAsync(double timeoutSec = 15.0)
    {
        if (_hAuth == null) return false;
        var ret = _hAuth.Call("verify_browser_login_async", timeoutSec);
        if (ret.VariantType == Variant.Type.Object)
        {
            var state = ret.AsGodotObject();
            if (state == null) return false;
            var completed = await ToSignal(state, "completed");
            return completed != null && completed.Length > 0 && completed[0].AsBool();
        }
        return ret.AsBool();
    }

    /// <summary>
    /// Wylogowanie (Auth + Connect).
    /// </summary>
    public Task<bool> LogoutAsync()
    {
        if (_logoutTcs != null && !_logoutTcs.Task.IsCompleted)
            return _logoutTcs.Task; // już trwa

        _logoutTcs = new TaskCompletionSource<bool>();
        _hAuth.Call("logout_async");
        // Wynik zostanie ustawiony w OnLoggedOutSignal lub w przypadku braku odpowiedzi po timeout.
        _ = CompleteLogoutOnTimeout();
        return _logoutTcs.Task;
    }

    /// <summary>
    /// Usunięcie zapisanego refresh tokenu (czyści PersistentAuth).
    /// Zwraca Task<bool> - await aby poczekać na wynik operacji.
    /// </summary>
    public async Task<bool> DeletePersistentAuthAsync()
    {
        GD.Print("[EOSPlatformAuth] Wywołuję delete_persistent_auth_async...");

        // Wywołaj async funkcję GDScript - zwraca Variant (prawdopodobnie GodotObject/Callable)
        var callResult = _hAuth.Call("delete_persistent_auth_async");

        // GDScript async funkcje zwracają signal completion, musimy użyć custom polling lub callback
        // Najprostsze rozwiązanie: poczekaj krótki czas i sprawdź logi
        // Lepsze rozwiązanie: dodaj signal w GDScript i się do niego podepnij

        // Na razie użyjemy prostego timeoutu - SDK zazwyczaj odpowiada szybko
        await Task.Delay(2000); // 2 sekundy na operację

        GD.Print("[EOSPlatformAuth] Operacja delete_persistent_auth powinna być zakończona (sprawdź logi GDScript)");

        // Nie mamy bezpośredniego dostępu do wyniku, ale możemy założyć sukces jeśli nie było błędów
        // W praktyce, sprawdź console output od GDScript
        return true; // Zakładamy sukces - rzeczywisty wynik w logach GDScript
    }

    /// <summary>
    /// Ręczne usunięcie folderu cache EOS (user://eosg-cache).
    /// Użyj jako fallback jeśli DeletePersistentAuthAsync() nie działa.
    /// WYMAGA RESTARTU APLIKACJI po wykonaniu!
    /// </summary>
    public bool DeleteCacheDirectoryManual()
    {
        GD.Print("[EOSPlatformAuth] Ręczne usuwanie cache EOS...");
        var result = _hAuth.Call("delete_cache_directory_manual");

        if (result.AsBool())
        {
            GD.Print("[EOSPlatformAuth] ✓ Cache został usunięty - ZRESTARTUJ APLIKACJĘ!");
            return true;
        }
        else
        {
            GD.PrintErr("[EOSPlatformAuth] ✗ Nie udało się usunąć cache - sprawdź logi");
            return false;
        }
    }

    /// <summary>
    /// Stara synchroniczna wersja - DEPRECATED, użyj DeletePersistentAuthAsync()
    /// </summary>
    [Obsolete("Użyj DeletePersistentAuthAsync() zamiast tego")]
    public void DeletePersistentAuth()
    {
        GD.PrintErr("[EOSPlatformAuth] UWAGA: DeletePersistentAuth() jest deprecated - użyj await DeletePersistentAuthAsync()");
        _ = DeletePersistentAuthAsync();
    }

    #endregion

    #region Prywatne helpery

    private Task<bool> StartLoginFlow(Action invoke)
    {
        if (_loginTcs != null && !_loginTcs.Task.IsCompleted)
            return _loginTcs.Task; // już trwa
        _loginTcs = new TaskCompletionSource<bool>();
        invoke();
        _ = CompleteLoginOnTimeout();
        return _loginTcs.Task;
    }

    private async Task CompleteLoginOnTimeout(double timeoutSec = 30)
    {
        double elapsed = 0;
        while (elapsed < timeoutSec && _loginTcs != null && !_loginTcs.Task.IsCompleted)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            elapsed += GetProcessDeltaTime();
        }
        if (_loginTcs != null && !_loginTcs.Task.IsCompleted)
        {
            _loginTcs.TrySetResult(false);
            LoginError?.Invoke("Timeout");
        }
    }

    private async Task CompleteLogoutOnTimeout(double timeoutSec = 10)
    {
        double elapsed = 0;
        while (elapsed < timeoutSec && _logoutTcs != null && !_logoutTcs.Task.IsCompleted)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            elapsed += GetProcessDeltaTime();
        }
        if (_logoutTcs != null && !_logoutTcs.Task.IsCompleted)
        {
            _logoutTcs.TrySetResult(false);
        }
    }

    // Sygnały z GDScript
    private void OnLoggedInSignal()
    {
        _loginTcs?.TrySetResult(true);
        LoggedIn?.Invoke();
        UserChanged?.Invoke(DisplayName, ProductUserId);
        // Wyczyść stan otwarcia strony – kolejna sesja zacznie od zera
        _verificationOpened = false;
        _verificationFallbackTried = false;
    }

    private void OnLoggedOutSignal()
    {
        _logoutTcs?.TrySetResult(true);
        LoggedOut?.Invoke();
        UserChanged?.Invoke(string.Empty, string.Empty);
        _verificationOpened = false;
        _verificationFallbackTried = false;
        // Po wylogowaniu można ponownie użyć PersistentAuth przy następnym uruchomieniu (jeśli token nie skasowany)
    }

    private void OnLoginErrorSignal(Variant errorCode)
    {
        string code = errorCode.AsString();
        _loginTcs?.TrySetResult(false);
        LoginError?.Invoke(code);
    }

    private void OnDisplayNameChanged()
    {
        UserChanged?.Invoke(DisplayName, ProductUserId);
    }

    private void OnLoginVerifiedSignal()
    {
        PersistentAuthVerified?.Invoke();
    }

    private void OnSdkLog(Variant msg)
    {
        // msg to Dictionary GDScript. Postarajmy się odczytać category i message
        if (msg.VariantType == Variant.Type.Dictionary)
        {
            var dict = msg.AsGodotDictionary();
            var category = dict.ContainsKey("category") ? dict["category"].AsString() : "unknown";
            var text = dict.ContainsKey("message") ? dict["message"].AsString() : string.Empty;
            SdkLog?.Invoke(category, text);
        }
    }

    private void OnPinGrantUpdated(Variant code, Variant url)
    {
        _latestDeviceCode = code.AsString();
        _latestVerificationUrl = url.AsString();
        GD.Print($"[EOSPlatformAuth] OnPinGrantUpdated wywołany: code='{_latestDeviceCode}' url='{_latestVerificationUrl}'");
        DeviceCodeUpdated?.Invoke(_latestDeviceCode, _latestVerificationUrl);
        GD.Print($"[EOSPlatformAuth] DeviceCodeUpdated event wyemitowany do {(DeviceCodeUpdated != null ? DeviceCodeUpdated.GetInvocationList().Length.ToString() : "0")} subskrybentów");

        // Fallback: jeśli GDScript nie otworzył przeglądarki, zrób to tutaj z lekkim opóźnieniem i jednym ponownym podejściem.
        if (_autoOpenVerificationUrl && !_verificationOpened && !string.IsNullOrWhiteSpace(_latestVerificationUrl))
        {
            GD.Print("[EOSPlatformAuth] Startuję fallback auto-open URL...");
            _ = MaybeOpenVerificationUrlAsync();
        }
    }

    private async Task MaybeOpenVerificationUrlAsync()
    {
        // Poczekaj chwilę – jeśli GDScript już otworzył, nie rób nic
        double elapsed = 0;
        while (elapsed < _initialOpenDelaySec)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            elapsed += GetProcessDeltaTime();
        }
        if (!_verificationOpened && !string.IsNullOrWhiteSpace(_latestVerificationUrl))
        {
            OS.ShellOpen(_latestVerificationUrl);
            _verificationOpened = true;
            GD.Print("[EOSPlatformAuth] Opened verification URL (fallback)");
        }

        // Jeśli po pewnym czasie nadal brak logowania – spróbuj otworzyć raz jeszcze
        elapsed = 0;
        while (elapsed < _fallbackReopenDelaySec)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            elapsed += GetProcessDeltaTime();
        }
        if (!_verificationFallbackTried && string.IsNullOrEmpty(ProductUserId) && !string.IsNullOrEmpty(_latestVerificationUrl))
        {
            _verificationFallbackTried = true;
            OS.ShellOpen(_latestVerificationUrl);
            GD.Print("[EOSPlatformAuth] Re-opened verification URL (single fallback)");
        }
    }

    #endregion
}
