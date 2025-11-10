using Godot;
using System;
using Epic.OnlineServices;
using Epic.OnlineServices.Platform;
using Epic.OnlineServices.Logging;
using Epic.OnlineServices.Auth;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Lobby;

public partial class EOSManager : Node
{
	// Sygnały dla UI
	[Signal]
	public delegate void LobbyListUpdatedEventHandler(Godot.Collections.Array<Godot.Collections.Dictionary> lobbies);
	
	[Signal]
	public delegate void LobbyJoinedEventHandler(string lobbyId);
	
	[Signal]
	public delegate void LobbyCreatedEventHandler(string lobbyId);
	
	[Signal]
	public delegate void LobbyCreationFailedEventHandler(string errorMessage);
	
	[Signal]
	public delegate void LobbyLeftEventHandler();

	[Signal]
	public delegate void CurrentLobbyInfoUpdatedEventHandler(string lobbyId, int currentPlayers, int maxPlayers, bool isOwner);
	
	[Signal]
	public delegate void LobbyMembersUpdatedEventHandler(Godot.Collections.Array<Godot.Collections.Dictionary> members);

	// Dane produktu
	private string productName = "WZIMniacy";
	private string productVersion = "1.0";

	// Dane uwierzytelniające EOS
	private string productId = "e0fad88fbfc147ddabce0900095c4f7b";
	private string sandboxId = "ce451c8e18ef4cb3bc7c5cdc11a9aaae";
	private string clientId = "xyza7891eEYHFtDWNZaFlmauAplnUo5H";
	private string clientSecret = "xD8rxykYUyqoaGoYZ5zhK+FD6Kg8+LvkATNkDb/7DPo";
	private string deploymentId = "0e28b5f3257a4dbca04ea0ca1c30f265";

	// Referencje do EOS
	private PlatformInterface platformInterface;
	private AuthInterface authInterface;
	private ConnectInterface connectInterface;
	private LobbyInterface lobbyInterface;
	
	// ID użytkownika - dla P2P używamy ProductUserId (Connect), dla Epic Account używamy EpicAccountId (Auth)
	private ProductUserId localProductUserId;  // P2P/Connect ID
	public string localProductUserIdString
    {
        get { return localProductUserId.ToString(); }
		set { localProductUserId = ProductUserId.FromString(value); }
    }  // P2P/Connect ID
	private EpicAccountId localEpicAccountId;  // Epic Account ID

	// Przechowywanie znalezionych lobby
	private System.Collections.Generic.List<string> foundLobbyIds = new System.Collections.Generic.List<string>();
	private System.Collections.Generic.Dictionary<string, LobbyDetails> foundLobbyDetails = new System.Collections.Generic.Dictionary<string, LobbyDetails>();

	// Obecne lobby w którym jesteśmy
	public string currentLobbyId = null;
	private bool isLobbyOwner = false;
	
	// Aktualna lista członków lobby (cache)
	private Godot.Collections.Array<Godot.Collections.Dictionary> currentLobbyMembers = new Godot.Collections.Array<Godot.Collections.Dictionary>();
	
	// Nickname ustawiony PRZED wejściem do lobby
	private string pendingNickname = "";
	
	// Flaga blokująca tworzenie lobby
	private bool isCreatingLobby = false;
	
	// Timer do odświeżania lobby
	private Timer lobbyRefreshTimer;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
    {
		GD.Print("=== Starting EOS Initialization ===");
		
		// Krok 1: Inicjalizacja SDK
        var initializeOptions = new InitializeOptions()
		{
			ProductName = productName,
			ProductVersion = productVersion,
		};

		GD.Print($"Product: {productName} v{productVersion}");
		
		var initializeResult = PlatformInterface.Initialize(ref initializeOptions);
		if (initializeResult != Result.Success)
        {
            GD.PrintErr("Failed to initialize EOS SDK: " + initializeResult);
			return;
        }

		GD.Print("✅ EOS SDK initialized successfully.");

		// Krok 2: Konfiguracja logowania
		LoggingInterface.SetLogLevel(LogCategory.AllCategories, LogLevel.VeryVerbose);
		LoggingInterface.SetCallback((ref LogMessage logMessage) =>
		{
			GD.Print($"[EOS {logMessage.Category}] {logMessage.Message}");
		});

		GD.Print("✅ Logging configured.");

		// Krok 3: Utworzenie platformy (PlatformHandle)
		var createOptions = new Options()
		{
			ProductId = productId,
			SandboxId = sandboxId,
			ClientCredentials = new ClientCredentials()
			{
				ClientId = clientId,
				ClientSecret = clientSecret
			},
			DeploymentId = deploymentId,
			IsServer = false,
			EncryptionKey = null,
			OverrideCountryCode = null,
			OverrideLocaleCode = null,
			Flags = PlatformFlags.DisableOverlay | PlatformFlags.LoadingInEditor
		};

		GD.Print($"Creating platform with ProductId: {productId}");
		GD.Print($"Sandbox: {sandboxId}, Deployment: {deploymentId}");

		platformInterface = PlatformInterface.Create(ref createOptions);
		if (platformInterface == null)
		{
			GD.PrintErr("❌ Failed to create EOS Platform Interface!");
			return;
		}

		GD.Print("✅ EOS Platform Interface created successfully.");

		// Pobierz Connect Interface (P2P, bez wymagania konta Epic)
		connectInterface = platformInterface.GetConnectInterface();
		if (connectInterface == null)
		{
			GD.PrintErr("Failed to get Connect Interface!");
			return;
		}

		// Pobierz Lobby Interface
		lobbyInterface = platformInterface.GetLobbyInterface();
		if (lobbyInterface == null)
		{
			GD.PrintErr("Failed to get Lobby Interface!");
			return;
		}

		// Dodaj nasłuchiwanie na zmiany w lobby (update członków)
		AddLobbyUpdateNotifications();
		
		// Stwórz timer do periodycznego odświeżania lobby
		CreateLobbyRefreshTimer();

		// Krok 4: Logowanie P2P (anonimowe, bez konta Epic)
		LoginWithDeviceId_P2P();
    }
	
	private void CreateLobbyRefreshTimer()
	{
		// USUNIĘTE: Automatyczne odświeżanie co 3 sekundy
		// Powód: SearchLobbies() zwraca LobbyDetails z pustymi UserID członków
		// Co powoduje błąd "Invalid member UserID!" i znikanie listy graczy
		// Zamiast tego używamy:
		// 1. Notyfikacji EOS (OnLobbyMemberUpdateReceived) - automatyczne aktualizacje gdy ktoś dołączy/wyjdzie
		// 2. Ręczne odświeżanie gdy użytkownik kliknie "Refresh" lub "Join"
		
		GD.Print("✅ Lobby notifications enabled (auto-refresh timer disabled)");
	}
	
	private void OnLobbyRefreshTimeout()
	{
		// WYŁĄCZONE - patrz komentarz w CreateLobbyRefreshTimer()
	}

	// Logowanie przez Device ID (Developer Tool - tylko do testów!)
	private void LoginWithDeviceId()
	{
		GD.Print("Starting Developer Auth login...");

		// UWAGA: Developer Auth wymaga Client Policy = "Trusted Server" w Epic Dev Portal
		// Alternatywnie można użyć AccountPortal (otwiera przeglądarkę)
		
		// Dla Developer Auth:
		// Id = localhost:port (adres DevAuthTool)
		// Token = nazwa użytkownika
		string devToolHost = "localhost:8080";
		string userName = "TestUser1";

		var loginOptions = new Epic.OnlineServices.Auth.LoginOptions()
		{
			Credentials = new Epic.OnlineServices.Auth.Credentials()
			{
				Type = LoginCredentialType.Developer,
				Id = devToolHost,     // Host:Port DevAuthTool
				Token = userName       // Nazwa użytkownika
			},
			ScopeFlags = AuthScopeFlags.BasicProfile | AuthScopeFlags.FriendsList | AuthScopeFlags.Presence
		};

		GD.Print($"Attempting Developer Auth login with DevTool at: {devToolHost}, User: {userName}");
		GD.Print("NOTE: Developer Auth requires Client Policy = 'Trusted Server' in Epic Dev Portal!");
		authInterface.Login(ref loginOptions, null, OnLoginComplete);
	}

	// Logowanie przez Account Portal (otwiera przeglądarkę Epic)
	private void LoginWithAccountPortal()
	{
		GD.Print("Starting Account Portal login...");

		var loginOptions = new Epic.OnlineServices.Auth.LoginOptions()
		{
			Credentials = new Epic.OnlineServices.Auth.Credentials()
			{
				Type = LoginCredentialType.AccountPortal,
				Id = null,
				Token = null
			},
			ScopeFlags = AuthScopeFlags.BasicProfile | AuthScopeFlags.FriendsList | AuthScopeFlags.Presence
		};

		GD.Print("Opening Epic Account login in browser...");
		authInterface.Login(ref loginOptions, null, OnLoginComplete);
	}

	// Logowanie przez Persistent Auth (używa zapamiętanych danych)
	private void LoginWithPersistentAuth()
	{
		GD.Print("Starting Persistent Auth login...");
		GD.Print("Trying to login with cached credentials...");

		var loginOptions = new Epic.OnlineServices.Auth.LoginOptions()
		{
			Credentials = new Epic.OnlineServices.Auth.Credentials()
			{
				Type = LoginCredentialType.PersistentAuth,
				Id = null,
				Token = null
			},
			ScopeFlags = AuthScopeFlags.BasicProfile | AuthScopeFlags.FriendsList | AuthScopeFlags.Presence
		};

		authInterface.Login(ref loginOptions, null, OnLoginComplete);
	}

	// ============================================
	// LOGOWANIE P2P (BEZ KONTA EPIC) - DeviceID
	// ============================================
	
	private void LoginWithDeviceId_P2P()
	{
		GD.Print("🎮 Starting P2P login (no Epic account required)...");
		
		// Generuj unikalny DeviceID dla tego urządzenia
		string deviceId = GetOrCreateDeviceId();
		GD.Print($"Device ID: {deviceId}");

		var createDeviceIdOptions = new CreateDeviceIdOptions()
		{
			DeviceModel = "PC"
		};

		// Najpierw utwórz DeviceID w systemie EOS
		connectInterface.CreateDeviceId(ref createDeviceIdOptions, null, (ref CreateDeviceIdCallbackInfo data) =>
		{
			if (data.ResultCode == Result.Success || data.ResultCode == Result.DuplicateNotAllowed)
			{
				// DeviceID istnieje lub został utworzony - teraz zaloguj się
				GD.Print("✅ DeviceID ready, logging in...");
				
				// WAŻNE: Dla DeviceidAccessToken, Token MUSI być null!
				var loginOptions = new Epic.OnlineServices.Connect.LoginOptions()
				{
					Credentials = new Epic.OnlineServices.Connect.Credentials()
					{
						Type = ExternalCredentialType.DeviceidAccessToken,
						Token = null  // MUSI być null dla DeviceID!
					},
					UserLoginInfo = new UserLoginInfo()
					{
						DisplayName = $"Player_{System.Environment.UserName}"
					}
				};

				connectInterface.Login(ref loginOptions, null, OnConnectLoginComplete);
			}
			else
			{
				GD.PrintErr($"❌ Failed to create DeviceID: {data.ResultCode}");
			}
		});
	}

	// Callback dla Connect Login (P2P)
	private void OnConnectLoginComplete(ref Epic.OnlineServices.Connect.LoginCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"✅ P2P Login successful! ProductUser ID: {data.LocalUserId}");
			localProductUserId = data.LocalUserId;
			
			// Gotowe do tworzenia lobby!
			GD.Print("🎮 Ready to create/join lobbies!");
			
			// Teraz możesz wywołać funkcje lobby
			// Przykład: CreateLobby("MyLobby", 4);
		}
		else
		{
			GD.PrintErr($"❌ P2P Login failed: {data.ResultCode}");
		}
	}

	// Generuj lub odczytaj DeviceID
	private string GetOrCreateDeviceId()
	{
		// Dla testowania wielu instancji na tym samym PC, dodaj losowy suffix
		// W produkcji możesz użyć tylko OS.GetUniqueId()
		string computerName = System.Environment.MachineName;
		string userName = System.Environment.UserName;
		string baseId = OS.GetUniqueId();
		
		// Dodaj losowy suffix żeby każda instancja miała unikalny ID
		int randomSuffix = (int)(GD.Randi() % 10000);
		
		return $"{computerName}_{userName}_{baseId}_{randomSuffix}";
	}

		// Callback po zakończeniu logowania
	private void OnLoginComplete(ref Epic.OnlineServices.Auth.LoginCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"✅ Login successful! User ID: {data.LocalUserId}");
			localEpicAccountId = data.LocalUserId;
			
			// Pobierz dodatkowe informacje o użytkowniku
			var copyUserAuthTokenOptions = new CopyUserAuthTokenOptions();
			Result result = authInterface.CopyUserAuthToken(ref copyUserAuthTokenOptions, data.LocalUserId, out Epic.OnlineServices.Auth.Token? authToken);
			
			if (result == Result.Success && authToken.HasValue)
			{
				GD.Print($"Account ID: {authToken.Value.AccountId}");
			}
		}
		else if (data.ResultCode == Result.InvalidUser)
		{
			// Brak zapisanych danych - przejdź na AccountPortal
			GD.Print($"⚠️ PersistentAuth failed ({data.ResultCode}), trying AccountPortal...");
			LoginWithAccountPortal();
		}
		else
		{
			GD.PrintErr($"❌ Login failed: {data.ResultCode}");
		}
	}

	// Pobierz informacje o zalogowanym użytkowniku
	private void GetUserInfo()
	{
		if (localEpicAccountId == null || !localEpicAccountId.IsValid())
		{
			GD.PrintErr("No valid user ID!");
			return;
		}

		var copyOptions = new CopyUserAuthTokenOptions();
		var result = authInterface.CopyUserAuthToken(ref copyOptions, localEpicAccountId, out var authToken);

		if (result == Result.Success && authToken != null)
		{
			GD.Print("=== User Info ===");
			GD.Print($"Account ID: {localEpicAccountId}");
			GD.Print($"App: {authToken?.App}");
			GD.Print($"Client ID: {authToken?.ClientId}");
			GD.Print("================");
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		// Krok 4: Tick platformy - musi być wywoływany regularnie
		if (platformInterface != null)
		{
			platformInterface.Tick();
		}
	}

	// Cleanup przy zamykaniu
	public override void _ExitTree()
	{
		// Wyloguj użytkownika przed zamknięciem (jeśli używamy Auth)
		if (authInterface != null && localEpicAccountId != null && localEpicAccountId.IsValid())
		{
			GD.Print("Logging out user...");
			var logoutOptions = new Epic.OnlineServices.Auth.LogoutOptions()
			{
				LocalUserId = localEpicAccountId
			};
			authInterface.Logout(ref logoutOptions, null, OnLogoutComplete);
		}

		if (platformInterface != null)
		{
			GD.Print("Releasing EOS Platform Interface...");
			platformInterface.Release();
			platformInterface = null;
		}

		PlatformInterface.Shutdown();
		GD.Print("EOS SDK shutdown complete.");
	}

	// Callback po wylogowaniu
	private void OnLogoutComplete(ref Epic.OnlineServices.Auth.LogoutCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print("✅ Logout successful!");
			localEpicAccountId = null;
		}
		else
		{
			GD.PrintErr($"❌ Logout failed: {data.ResultCode}");
		}
	}

	// ============================================
	// NICKNAME MANAGEMENT
	// ============================================
	
	/// <summary>
	/// Ustawia nickname który będzie użyty przy dołączeniu/utworzeniu lobby
	/// </summary>
	/// <param name="nickname">Nickname gracza (2-20 znaków)</param>
	public void SetPendingNickname(string nickname)
	{
		if (string.IsNullOrWhiteSpace(nickname))
		{
			GD.Print("⚠️ Nickname is empty, will use fallback");
			pendingNickname = "";
			return;
		}
		
		// Sanitizacja
		nickname = nickname.Trim();
		if (nickname.Length < 2) nickname = nickname.PadRight(2, '_');
		if (nickname.Length > 20) nickname = nickname.Substring(0, 20);
		
		// Filtruj znaki (zostaw tylko litery, cyfry, _, -)
		char[] filtered = Array.FindAll(nickname.ToCharArray(), c => char.IsLetterOrDigit(c) || c == '_' || c == '-');
		string sanitized = new string(filtered);
		
		if (string.IsNullOrEmpty(sanitized))
		{
			GD.Print("⚠️ Nickname contains only invalid characters, will use fallback");
			pendingNickname = "";
			return;
		}
		
		pendingNickname = sanitized;
		GD.Print($"✅ Pending nickname set to: {pendingNickname}");
	}
	
	/// <summary>
	/// Zwraca aktualnie ustawiony pending nickname (dla UI)
	/// </summary>
	public string GetPendingNickname()
	{
		return pendingNickname;
	}

	// ============================================
	// SYSTEM LOBBY - Tworzenie, wyszukiwanie, dołączanie
	// ============================================

	/// <summary>
	/// Tworzy nowe lobby
	/// </summary>
	/// <param name="lobbyName">Nazwa lobby (opcjonalnie, może być puste)</param>
	/// <param name="maxPlayers">Maksymalna liczba graczy (2-64)</param>
	/// <param name="isPublic">Czy lobby jest publiczne (można wyszukać)?</param>
	public void CreateLobby(string lobbyName, uint maxPlayers = 4, bool isPublic = true)
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("❌ Cannot create lobby: User not logged in!");
			EmitSignal(SignalName.LobbyCreationFailed, "User not logged in");
			return;
		}

		// Sprawdź czy użytkownik już jest w lobby
		if (!string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintRich("[color=yellow]❌ Cannot create lobby: You are already in a lobby!");
			GD.PrintRich($"[color=yellow]   Current lobby: {currentLobbyId} (Owner: {isLobbyOwner})");
			GD.PrintRich("[color=yellow]   Please leave the current lobby first.");
			EmitSignal(SignalName.LobbyCreationFailed, "Already in a lobby");
			return;
		}

		// NOWE: Sprawdź czy lobby już jest tworzone
		if (isCreatingLobby)
		{
			GD.PrintErr("❌ Cannot create lobby: Lobby creation already in progress!");
			EmitSignal(SignalName.LobbyCreationFailed, "Lobby creation already in progress");
			return;
		}

		GD.Print($"🏗️ Creating lobby: {lobbyName}, Max players: {maxPlayers}, Public: {isPublic}");
		
		// Zablokuj tworzenie lobby
		isCreatingLobby = true;

		var createLobbyOptions = new CreateLobbyOptions()
		{
			LocalUserId = localProductUserId,
			MaxLobbyMembers = maxPlayers,
			PermissionLevel = isPublic ? LobbyPermissionLevel.Publicadvertised : LobbyPermissionLevel.Inviteonly,
			PresenceEnabled = false, // Wyłączamy presence (nie potrzebujemy Epic Friends)
			AllowInvites = true,
			BucketId = "DefaultBucket", // Bucket do filtrowania lobby
			DisableHostMigration = false,
			EnableRTCRoom = false // Wyłączamy voice chat na razie
		};

		lobbyInterface.CreateLobby(ref createLobbyOptions, null, OnCreateLobbyComplete);
	}

	private void OnCreateLobbyComplete(ref CreateLobbyCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"✅ Lobby created successfully! Lobby ID: {data.LobbyId}");
			
			// Zapisz obecne lobby
			currentLobbyId = data.LobbyId.ToString();
			isLobbyOwner = true;
			// NOWE: Natychmiast skopiuj LobbyDetails handle bez wykonywania SearchLobbies()
			CacheCurrentLobbyDetailsHandle("create");
			
			// Wyślij sygnał do UI
			EmitSignal(SignalName.LobbyCreated, currentLobbyId);
			
			// Wyślij info o obecnym lobby (1 gracz = właściciel, 4 max)
			EmitSignal(SignalName.CurrentLobbyInfoUpdated, currentLobbyId, 1, 4, true);
			
			// Ustaw nickname jako member attribute (jeśli został ustawiony)
			if (!string.IsNullOrEmpty(pendingNickname))
			{
				GetTree().CreateTimer(1.0).Timeout += () =>
				{
					SetMemberAttribute("Nickname", pendingNickname);
					// Po ustawieniu, odśwież listę członków
					GetTree().CreateTimer(1.0).Timeout += () =>
					{
						GetLobbyMembers();
					};
				};
			}
			
			// NOWE: Wyślij pustą listę członków najpierw (z fallbackiem)
			// Bo SearchLobbies() zajmuje czas i nie znajdzie naszego lobby od razu
			var tempMembersList = new Godot.Collections.Array<Godot.Collections.Dictionary>();
			
			string displayName = !string.IsNullOrEmpty(pendingNickname) 
				? pendingNickname 
				: $"Player_{localProductUserId.ToString().Substring(Math.Max(0, localProductUserId.ToString().Length - 8))}";
			
			var tempMemberData = new Godot.Collections.Dictionary
			{
				{ "userId", localProductUserId.ToString() },
				{ "displayName", displayName },
				{ "isOwner", true },
				{ "isLocalPlayer", true }
			};
			tempMembersList.Add(tempMemberData);
			
			// Zapisz do cache
			currentLobbyMembers = tempMembersList;
			
			EmitSignal(SignalName.LobbyMembersUpdated, tempMembersList);
			GD.Print($"👥 Sent initial member list (1 member - you)"); // Możesz teraz ustawić atrybuty lobby (nazwa, mapa, tryb gry itp.)
			// SetLobbyAttribute(data.LobbyId, "LobbyName", "My Awesome Lobby");
		}
		else
		{
			GD.PrintErr($"❌ Failed to create lobby: {data.ResultCode}");
			
			// Wyślij sygnał o błędzie do UI
			EmitSignal(SignalName.LobbyCreationFailed, data.ResultCode.ToString());
		}
		
		// NOWE: Odblokuj tworzenie lobby (niezależnie od sukcesu czy błędu)
		isCreatingLobby = false;
	}

	/// <summary>
	/// Wyszukuje dostępne lobby
	/// </summary>
	public void SearchLobbies()
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("❌ Cannot search lobbies: User not logged in!");
			return;
		}

		GD.Print("🔍 Searching for lobbies...");

		// Utwórz wyszukiwanie
		var createLobbySearchOptions = new CreateLobbySearchOptions()
		{
			MaxResults = 25 // Maksymalnie 25 wyników
		};

		Result result = lobbyInterface.CreateLobbySearch(ref createLobbySearchOptions, out LobbySearch lobbySearch);
		
		if (result != Result.Success || lobbySearch == null)
		{
			GD.PrintErr($"❌ Failed to create lobby search: {result}");
			return;
		}

		// Ustaw filtr - tylko publiczne lobby
		var searchSetParameterOptions = new LobbySearchSetParameterOptions()
		{
			ComparisonOp = ComparisonOp.Equal,
			Parameter = new AttributeData()
			{
				Key = "bucket",
				Value = new AttributeDataValue() { AsUtf8 = "DefaultBucket" }
			}
		};

		lobbySearch.SetParameter(ref searchSetParameterOptions);

		// Rozpocznij wyszukiwanie
		var findOptions = new LobbySearchFindOptions()
		{
			LocalUserId = localProductUserId
		};

		lobbySearch.Find(ref findOptions, null, (ref LobbySearchFindCallbackInfo findData) =>
		{
			if (findData.ResultCode == Result.Success)
			{
				var countOptions = new LobbySearchGetSearchResultCountOptions();
				uint lobbyCount = lobbySearch.GetSearchResultCount(ref countOptions);
				GD.Print($"✅ Found {lobbyCount} lobbies!");

				// Wyczyść listę przed dodaniem nowych
				foundLobbyIds.Clear();
				
				// Zwolnij stare LobbyDetails przed dodaniem nowych
				foreach (var details in foundLobbyDetails.Values)
				{
					details.Release();
				}
				foundLobbyDetails.Clear();

				// Lista lobby do wysłania do UI
				var lobbyList = new Godot.Collections.Array<Godot.Collections.Dictionary>();

				// Wyświetl wszystkie znalezione lobby
				for (uint i = 0; i < lobbyCount; i++)
				{
					var copyOptions = new LobbySearchCopySearchResultByIndexOptions() { LobbyIndex = i };
					Result copyResult = lobbySearch.CopySearchResultByIndex(ref copyOptions, out LobbyDetails lobbyDetails);
					
					if (copyResult == Result.Success && lobbyDetails != null)
					{
						var infoOptions = new LobbyDetailsCopyInfoOptions();
						lobbyDetails.CopyInfo(ref infoOptions, out LobbyDetailsInfo? info);
						
						if (info != null)
						{
							foundLobbyIds.Add(info.Value.LobbyId);
							foundLobbyDetails[info.Value.LobbyId] = lobbyDetails; // Zapisz LobbyDetails
							
							// Pobierz rzeczywistą liczbę członków z LobbyDetails
							var memberCountOptions = new LobbyDetailsGetMemberCountOptions();
							uint actualMemberCount = lobbyDetails.GetMemberCount(ref memberCountOptions);
							int currentPlayers = (int)actualMemberCount;
							
							GD.Print($"  [{i}] Lobby ID: {info.Value.LobbyId}, Players: {currentPlayers}/{info.Value.MaxMembers}");

							// Dodaj do listy dla UI
							var lobbyData = new Godot.Collections.Dictionary
							{
								{ "index", (int)i },
								{ "lobbyId", info.Value.LobbyId.ToString() },
								{ "currentPlayers", currentPlayers },
								{ "maxPlayers", (int)info.Value.MaxMembers },
								{ "owner", info.Value.LobbyOwnerUserId?.ToString() ?? "Unknown" }
							};
							lobbyList.Add(lobbyData);
						}
						else
						{
							lobbyDetails.Release();
						}
					}
				}

				// Wyślij sygnał do UI z listą lobby
				EmitSignal(SignalName.LobbyListUpdated, lobbyList);
			}
			else
			{
				GD.PrintErr($"❌ Lobby search failed: {findData.ResultCode}");
			}

			lobbySearch.Release();
		});
	}

	/// <summary>
	/// Dołącza do lobby po indeksie z ostatniego wyszukania
	/// </summary>
	/// <param name="lobbyIndex">Indeks lobby z listy (0, 1, 2...)</param>
	public void JoinLobbyByIndex(int lobbyIndex)
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("❌ Cannot join lobby: User not logged in!");
			return;
		}

		if (lobbyIndex < 0 || lobbyIndex >= foundLobbyIds.Count)
		{
			GD.PrintErr($"❌ Invalid lobby index: {lobbyIndex}. Found lobbies: {foundLobbyIds.Count}");
			return;
		}

		string lobbyId = foundLobbyIds[lobbyIndex];
		JoinLobby(lobbyId);
	}

	/// <summary>
	/// Dołącza do lobby po ID
	/// </summary>
	/// <param name="lobbyId">ID lobby do dołączenia</param>
	public void JoinLobby(string lobbyId)
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("❌ Cannot join lobby: User not logged in!");
			return;
		}

		// Sprawdź czy użytkownik już jest w lobby
		if (!string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("❌ Cannot join lobby: You are already in a lobby!");
			GD.PrintErr($"   Current lobby: {currentLobbyId} (Owner: {isLobbyOwner})");
			GD.PrintErr("   Please leave the current lobby first.");
			return;
		}

		if (!foundLobbyDetails.ContainsKey(lobbyId))
		{
			GD.PrintErr($"❌ Lobby details not found for ID: {lobbyId}. Search for lobbies first!");
			return;
		}

		GD.Print($"🚪 Joining lobby: {lobbyId}");

		var joinLobbyOptions = new JoinLobbyOptions()
		{
			LobbyDetailsHandle = foundLobbyDetails[lobbyId],
			LocalUserId = localProductUserId,
			PresenceEnabled = false
		};

		lobbyInterface.JoinLobby(ref joinLobbyOptions, null, OnJoinLobbyComplete);
	}

	private void OnJoinLobbyComplete(ref JoinLobbyCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"✅ Successfully joined lobby: {data.LobbyId}");
			
		// Zapisz obecne lobby
		currentLobbyId = data.LobbyId.ToString();
		isLobbyOwner = false;
		// NOWE: Skopiuj LobbyDetails handle natychmiast – eliminuje wyścig z pierwszymi callbackami
		CacheCurrentLobbyDetailsHandle("join");
		
		// Wyślij sygnał do UI
		EmitSignal(SignalName.LobbyJoined, currentLobbyId);
		
		// Ustaw nickname jako member attribute (jeśli został ustawiony)
		if (!string.IsNullOrEmpty(pendingNickname))
		{
			GetTree().CreateTimer(1.0).Timeout += () =>
			{
				SetMemberAttribute("Nickname", pendingNickname);
			};
		}
		
		// NOWE: Wyślij tymczasową listę członków (tylko ty)
		// Bo SearchLobbies() zajmie czas
		var tempMembersList = new Godot.Collections.Array<Godot.Collections.Dictionary>();
		
		string displayName = !string.IsNullOrEmpty(pendingNickname) 
			? pendingNickname 
			: $"Player_{localProductUserId.ToString().Substring(Math.Max(0, localProductUserId.ToString().Length - 8))}";
		
		var tempMemberData = new Godot.Collections.Dictionary
		{
			{ "userId", localProductUserId.ToString() },
			{ "displayName", displayName },
			{ "isOwner", false },
			{ "isLocalPlayer", true }
		};
		tempMembersList.Add(tempMemberData);
		
		// Zapisz do cache
		currentLobbyMembers = tempMembersList;
		
		EmitSignal(SignalName.LobbyMembersUpdated, tempMembersList);
		GD.Print($"👥 Sent initial member list (1 member - you)");
		
		// WAŻNE: Po dołączeniu, wykonaj ponowne wyszukiwanie żeby zaktualizować LobbyDetails
		// To zapewni, że będziemy mieli aktualną liczbę graczy
		CallDeferred(nameof(SearchLobbiesAndRefresh));
		}
		else
		{
			GD.PrintErr($"❌ Failed to join lobby: {data.ResultCode}");
		}
	}
	
	/// <summary>
	/// Wyszukuje lobby i odświeża info o obecnym lobby
	/// FAKTYCZNIE wykonuje LobbySearch.Find() żeby pobrać świeże dane z backendu
	/// </summary>
	private void SearchLobbiesAndRefresh()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.Print("⚠️ Cannot refresh - no current lobby ID");
			return;
		}
		
		// Czekamy chwilę żeby backend zdążył zsynchronizować dane
		GetTree().CreateTimer(1.5).Timeout += () => 
		{
			GD.Print($"🔍 Searching for current lobby {currentLobbyId} to get fresh data...");
			
			var createLobbySearchOptions = new Epic.OnlineServices.Lobby.CreateLobbySearchOptions
			{
				MaxResults = 100
			};
			
			var searchResult = lobbyInterface.CreateLobbySearch(ref createLobbySearchOptions, out var lobbySearchHandle);
			if (searchResult != Epic.OnlineServices.Result.Success || lobbySearchHandle == null)
			{
				GD.PrintErr($"❌ Failed to create lobby search: {searchResult}");
				return;
			}
			
			// Szukaj po konkretnym LobbyId
			var setLobbyIdOptions = new Epic.OnlineServices.Lobby.LobbySearchSetLobbyIdOptions
			{
				LobbyId = currentLobbyId
			};
			
			var setIdResult = lobbySearchHandle.SetLobbyId(ref setLobbyIdOptions);
			if (setIdResult != Epic.OnlineServices.Result.Success)
			{
				GD.PrintErr($"❌ Failed to set lobby ID filter: {setIdResult}");
				return;
			}
			
			// Wykonaj search (pobiera dane z backendu!)
			var findOptions = new Epic.OnlineServices.Lobby.LobbySearchFindOptions
			{
				LocalUserId = localProductUserId
			};
			
			lobbySearchHandle.Find(ref findOptions, null, (ref Epic.OnlineServices.Lobby.LobbySearchFindCallbackInfo data) =>
			{
				if (data.ResultCode != Epic.OnlineServices.Result.Success)
				{
					GD.PrintErr($"❌ Lobby search failed: {data.ResultCode}");
					return;
				}
				
				var getSearchResultCountOptions = new Epic.OnlineServices.Lobby.LobbySearchGetSearchResultCountOptions();
				uint resultCount = lobbySearchHandle.GetSearchResultCount(ref getSearchResultCountOptions);
				
				if (resultCount == 0)
				{
					GD.PrintErr("❌ Current lobby not found in search results");
					return;
				}
				
				GD.Print($"✅ Found current lobby, getting fresh LobbyDetails handle...");
				
				// Pobierz ŚWIEŻY handle z wyników search
				var copyResultOptions = new Epic.OnlineServices.Lobby.LobbySearchCopySearchResultByIndexOptions
				{
					LobbyIndex = 0
				};
				
				var copyResult = lobbySearchHandle.CopySearchResultByIndex(ref copyResultOptions, out var freshLobbyDetails);
				if (copyResult != Epic.OnlineServices.Result.Success || freshLobbyDetails == null)
				{
					GD.PrintErr($"❌ Failed to copy search result: {copyResult}");
					return;
				}
				
				// ⚠️ NIE nadpisuj handle jeśli już działa! 
				// Handle z WebSocket (member_update) ma pełne dane, a ten z search może być pusty
				if (!foundLobbyDetails.ContainsKey(currentLobbyId))
				{
					foundLobbyDetails[currentLobbyId] = freshLobbyDetails;
					GD.Print("✅ LobbyDetails handle added from backend!");
				}
				else
				{
					// Sprawdź czy nowy handle ma RZECZYWISTE dane (nie tylko count)
					var testOptions = new LobbyDetailsGetMemberCountOptions();
					uint newCount = freshLobbyDetails.GetMemberCount(ref testOptions);
					uint oldCount = foundLobbyDetails[currentLobbyId].GetMemberCount(ref testOptions);
					
					GD.Print($"   Comparing handles: Old={oldCount} members, New={newCount} members");
					
					// Testuj czy GetMemberByIndex działa na NOWYM handle
					bool newHandleValid = false;
					if (newCount > 0)
					{
						var testMemberOptions = new LobbyDetailsGetMemberByIndexOptions() { MemberIndex = 0 };
						ProductUserId testUserId = freshLobbyDetails.GetMemberByIndex(ref testMemberOptions);
						newHandleValid = testUserId != null && testUserId.IsValid();
						GD.Print($"   New handle validity test: UserID={(testUserId != null ? testUserId.ToString() : "NULL")} Valid={newHandleValid}");
					}
					
					// Tylko zamień jeśli nowy handle FAKTYCZNIE działa
					if (newHandleValid && newCount >= oldCount)
					{
						foundLobbyDetails[currentLobbyId]?.Release();
						foundLobbyDetails[currentLobbyId] = freshLobbyDetails;
						GD.Print("✅ LobbyDetails handle refreshed from backend (validated)!");
					}
					else
					{
						freshLobbyDetails?.Release();
						GD.Print("⚠️ Keeping old handle (new handle invalid or has less data)");
					}
				}
				
				// Teraz możemy bezpiecznie odczytać członków
				CallDeferred(nameof(RefreshCurrentLobbyInfo));
				CallDeferred(nameof(GetLobbyMembers));
			});
		};
	}

	/// <summary>
	/// Opuszcza obecne lobby
	/// </summary>
	/// <param name="lobbyId">ID lobby do opuszczenia</param>
	/// <summary>
	/// Opuszcza obecne lobby
	/// </summary>
	public void LeaveLobby()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("❌ Cannot leave lobby: Not in any lobby!");
			return;
		}
		
		LeaveLobby(currentLobbyId);
	}
	
	/// <summary>
	/// Opuszcza lobby po ID
	/// </summary>
	public void LeaveLobby(string lobbyId)
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("❌ Cannot leave lobby: User not logged in!");
			return;
		}

		GD.Print($"🚪 Leaving lobby: {lobbyId}");

		var leaveLobbyOptions = new LeaveLobbyOptions()
		{
			LobbyId = lobbyId,
			LocalUserId = localProductUserId
		};

		lobbyInterface.LeaveLobby(ref leaveLobbyOptions, null, OnLeaveLobbyComplete);
	}

	private void OnLeaveLobbyComplete(ref LeaveLobbyCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"✅ Successfully left lobby: {data.LobbyId}");
			
			// Zatrzymaj timer
			if (lobbyRefreshTimer != null && lobbyRefreshTimer.TimeLeft > 0)
			{
				lobbyRefreshTimer.Stop();
				GD.Print("🛑 Lobby refresh timer stopped");
			}
			
			// Wyczyść obecne lobby
			currentLobbyId = null;
			isLobbyOwner = false;
			
			// Wyczyść cache członków
			currentLobbyMembers.Clear();
			
			// Wyczyść flagę tworzenia (na wszelki wypadek)
			isCreatingLobby = false;
			
			// Wyślij sygnał do UI
			EmitSignal(SignalName.LobbyLeft);
		}
		else
		{
			GD.PrintErr($"❌ Failed to leave lobby: {data.ResultCode}");
		}
	}

	// ============================================
	// NASŁUCHIWANIE NA ZMIANY W LOBBY
	// ============================================

	private ulong lobbyUpdateNotificationId = 0;
	private ulong lobbyMemberUpdateNotificationId = 0;
	private ulong lobbyMemberStatusNotificationId = 0;

	private void AddLobbyUpdateNotifications()
	{
		// Nasłuchuj na zmiany w lobby (np. nowy gracz dołączył)
		var addNotifyOptions = new AddNotifyLobbyUpdateReceivedOptions();
		lobbyUpdateNotificationId = lobbyInterface.AddNotifyLobbyUpdateReceived(ref addNotifyOptions, null, OnLobbyUpdateReceived);

		// Nasłuchuj na zmiany członków lobby (aktualizacje atrybutów)
		var memberUpdateOptions = new AddNotifyLobbyMemberUpdateReceivedOptions();
		lobbyMemberUpdateNotificationId = lobbyInterface.AddNotifyLobbyMemberUpdateReceived(ref memberUpdateOptions, null, OnLobbyMemberUpdateReceived);

		// Nasłuchuj na status członków (dołączenie/opuszczenie)
		var memberStatusOptions = new AddNotifyLobbyMemberStatusReceivedOptions();
		lobbyMemberStatusNotificationId = lobbyInterface.AddNotifyLobbyMemberStatusReceived(ref memberStatusOptions, null, OnLobbyMemberStatusReceived);

		GD.Print("✅ Lobby update notifications added");
	}

	private void OnLobbyUpdateReceived(ref LobbyUpdateReceivedCallbackInfo data)
	{
		GD.Print($"🔔 Lobby updated: {data.LobbyId}");
		
		// Jeśli to nasze lobby, odśwież info
		if (currentLobbyId == data.LobbyId.ToString())
		{
			RefreshCurrentLobbyInfo();
		}
	}

private void OnLobbyMemberUpdateReceived(ref LobbyMemberUpdateReceivedCallbackInfo data)
{
	GD.Print($"🔔 Lobby member updated in: {data.LobbyId}, User: {data.TargetUserId}");
	if (currentLobbyId != data.LobbyId.ToString()) return;
	
	GD.Print("  ℹ️ Member update detected - refreshing member list");
	
	// Odśwież LobbyDetails handle i listę członków
	CacheCurrentLobbyDetailsHandle("member_update");
	
	// Małe opóźnienie na synchronizację EOS
	GetTree().CreateTimer(0.5).Timeout += () =>
	{
		GetLobbyMembers();
	};
}

	private void OnLobbyMemberStatusReceived(ref LobbyMemberStatusReceivedCallbackInfo data)
	{
		GD.Print($"🔔 Lobby member status changed in: {data.LobbyId}, User: {data.TargetUserId}, Status: {data.CurrentStatus}");
		
		// Jeśli to nasze lobby
		if (currentLobbyId == data.LobbyId.ToString())
		{
			// Odśwież LobbyDetails handle
			CacheCurrentLobbyDetailsHandle("member_status");
			
			string userId = data.TargetUserId.ToString();
			
			// JOINED lub LEFT - odśwież całą listę członków
			if (data.CurrentStatus == LobbyMemberStatus.Joined)
			{
				GD.Print($"  ➕ Member JOINED: {userId.Substring(Math.Max(0, userId.Length - 8))}");
				
				// Małe opóźnienie na synchronizację EOS
				GetTree().CreateTimer(0.5).Timeout += () =>
				{
					GetLobbyMembers();
					EmitSignal(SignalName.CurrentLobbyInfoUpdated, currentLobbyId, currentLobbyMembers.Count, 4, isLobbyOwner);
				};
			}
			else if (data.CurrentStatus == LobbyMemberStatus.Left)
			{
				GD.Print($"  ➖ Member LEFT: {userId.Substring(Math.Max(0, userId.Length - 8))}");
				
				// Małe opóźnienie na synchronizację EOS
				GetTree().CreateTimer(0.5).Timeout += () =>
				{
					GetLobbyMembers();
					EmitSignal(SignalName.CurrentLobbyInfoUpdated, currentLobbyId, currentLobbyMembers.Count, 4, isLobbyOwner);
				};
			}
		}
	}

	/// <summary>
	/// Odświeża informacje o obecnym lobby i wysyła sygnał do UI
	/// </summary>
	private void RefreshCurrentLobbyInfo()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			return;
		}

		// Sprawdź czy mamy lobby details
		if (!foundLobbyDetails.ContainsKey(currentLobbyId))
		{
			// Jeśli nie ma w cache, spróbuj skopiować bez wyszukiwania (redukcja zależności od search)
			CacheCurrentLobbyDetailsHandle("refresh_info");
			if (!foundLobbyDetails.ContainsKey(currentLobbyId)) return;
		}

		LobbyDetails lobbyDetails = foundLobbyDetails[currentLobbyId];
		
		if (lobbyDetails != null)
		{
			var infoOptions = new LobbyDetailsCopyInfoOptions();
			lobbyDetails.CopyInfo(ref infoOptions, out LobbyDetailsInfo? info);
			
			if (info != null)
			{
				// Pobierz rzeczywistą liczbę członków
				var memberCountOptions = new LobbyDetailsGetMemberCountOptions();
				uint memberCount = lobbyDetails.GetMemberCount(ref memberCountOptions);
				
				GD.Print($"📊 Lobby info refreshed: {currentLobbyId}, Players: {memberCount}/{info.Value.MaxMembers}");
				
				// Wyślij sygnał do UI
				EmitSignal(SignalName.CurrentLobbyInfoUpdated, 
					currentLobbyId, 
					(int)memberCount, 
					(int)info.Value.MaxMembers, 
					isLobbyOwner);
				
				// NOWE: Odśwież też listę członków
				// GetLobbyMembers(); // WYŁĄCZONE - puste UserID z SearchLobbies()
			}
		}
		else
		{
			GD.PrintErr($"❌ Failed to refresh lobby info - lobby details is null");
		}
	}

	/// <summary>
	/// Pobiera rzeczywistą liczbę członków w lobby (użyj po dołączeniu lub przy wyszukiwaniu)
	/// </summary>
	public int GetLobbyMemberCount(string lobbyId)
	{
		if (!foundLobbyDetails.ContainsKey(lobbyId))
		{
			GD.PrintErr($"❌ Lobby details not found for ID: {lobbyId}");
			return 0;
		}

		var countOptions = new LobbyDetailsGetMemberCountOptions();
		uint memberCount = foundLobbyDetails[lobbyId].GetMemberCount(ref countOptions);
		
		return (int)memberCount;
	}
	
	// ============================================
	// MEMBER ATTRIBUTES
	// ============================================
	
	/// <summary>
	/// Ustawia member attribute dla lokalnego gracza w obecnym lobby
	/// </summary>
	/// <param name="key">Klucz atrybutu (np. "Nickname")</param>
	/// <param name="value">Wartość atrybutu</param>
	private void SetMemberAttribute(string key, string value)
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("❌ Cannot set member attribute: Not in any lobby!");
			return;
		}
		
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("❌ Cannot set member attribute: User not logged in!");
			return;
		}
		
		GD.Print($"📝 Setting member attribute: {key} = '{value}'");
		
		var modifyOptions = new UpdateLobbyModificationOptions()
		{
			LobbyId = currentLobbyId,
			LocalUserId = localProductUserId
		};
		
		Result result = lobbyInterface.UpdateLobbyModification(ref modifyOptions, out LobbyModification lobbyModification);
		
		if (result != Result.Success || lobbyModification == null)
		{
			GD.PrintErr($"❌ Failed to create lobby modification: {result}");
			return;
		}
		
		// Dodaj member attribute
		var attributeData = new AttributeData()
		{
			Key = key,
			Value = new AttributeDataValue() { AsUtf8 = value }
		};
		
		var addMemberAttrOptions = new LobbyModificationAddMemberAttributeOptions()
		{
			Attribute = attributeData,
			Visibility = LobbyAttributeVisibility.Public
		};
		
		result = lobbyModification.AddMemberAttribute(ref addMemberAttrOptions);
		
		if (result != Result.Success)
		{
			GD.PrintErr($"❌ Failed to add member attribute '{key}': {result}");
			lobbyModification.Release();
			return;
		}
		
		// Wyślij modyfikację do EOS
		var updateOptions = new UpdateLobbyOptions()
		{
			LobbyModificationHandle = lobbyModification
		};
		
		lobbyInterface.UpdateLobby(ref updateOptions, null, (ref UpdateLobbyCallbackInfo data) =>
		{
			if (data.ResultCode == Result.Success)
			{
				GD.Print($"✅ Member attribute '{key}' set successfully: '{value}'");
			}
			else
			{
				GD.PrintErr($"❌ Failed to update member attribute '{key}': {data.ResultCode}");
			}
			
			lobbyModification.Release();
		});
	}
	
	/// <summary>
	/// Pobiera listę członków obecnego lobby i wysyła sygnał do UI
	/// </summary>
	public void GetLobbyMembers()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("❌ Cannot get lobby members: Not in any lobby!");
			return;
		}

		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("❌ Cannot get lobby members: User not logged in!");
			return;
		}

		// Sprawdź czy mamy lobby details w cache
		if (!foundLobbyDetails.ContainsKey(currentLobbyId))
		{
			GD.PrintErr($"❌ Lobby details not found in cache for ID: {currentLobbyId}");
			GD.Print($"   Available lobbies in cache: {string.Join(", ", foundLobbyDetails.Keys)}");
			return;
		}

		LobbyDetails lobbyDetails = foundLobbyDetails[currentLobbyId];
		
		if (lobbyDetails == null)
		{
			GD.PrintErr("❌ Lobby details is null!");
			return;
		}
		
		// Pobierz liczbę członków
		var countOptions = new LobbyDetailsGetMemberCountOptions();
		uint memberCount = lobbyDetails.GetMemberCount(ref countOptions);
		
		GD.Print($"👥 Getting {memberCount} lobby members from lobby {currentLobbyId}...");
		
		// Lista członków do wysłania do UI
		var membersList = new Godot.Collections.Array<Godot.Collections.Dictionary>();
		
		// Iteruj po wszystkich członkach
		for (uint i = 0; i < memberCount; i++)
		{
			var memberByIndexOptions = new LobbyDetailsGetMemberByIndexOptions() { MemberIndex = i };
			ProductUserId memberUserId = lobbyDetails.GetMemberByIndex(ref memberByIndexOptions);
			
			GD.Print($"  Member {i}: UserID={memberUserId}");
			
			if (memberUserId != null && memberUserId.IsValid())
			{
				// Pobierz informacje o członku
				var memberInfoOptions = new LobbyDetailsGetMemberAttributeCountOptions() { TargetUserId = memberUserId };
				uint attributeCount = lobbyDetails.GetMemberAttributeCount(ref memberInfoOptions);
				
				GD.Print($"    AttributeCount={attributeCount}");
				
				// Pobierz Nickname (jeśli ustawiony)
				string displayName = null;
				bool foundNickname = false;
				
				// Próbuj pobrać Nickname z atrybutów członka
				for (uint j = 0; j < attributeCount; j++)
				{
					var attrOptions = new LobbyDetailsCopyMemberAttributeByIndexOptions() 
					{ 
						TargetUserId = memberUserId,
						AttrIndex = j 
					};
					
					Result attrResult = lobbyDetails.CopyMemberAttributeByIndex(ref attrOptions, out Epic.OnlineServices.Lobby.Attribute? attribute);
					
					if (attrResult == Result.Success && attribute.HasValue)
					{
						GD.Print($"      Attribute: {attribute.Value.Data?.Key} = {attribute.Value.Data?.Value.AsUtf8}");
						
						if (attribute.Value.Data.HasValue)
						{
							string keyStr = attribute.Value.Data.Value.Key;
							if (keyStr != null && keyStr.Equals("Nickname", System.StringComparison.OrdinalIgnoreCase))
							{
								displayName = attribute.Value.Data.Value.Value.AsUtf8;
								foundNickname = true;
								break;
							}
						}
					}
				}
				
				// Jeśli nie znaleziono Nickname, użyj fallback (skrócony ProductUserId)
				if (!foundNickname)
				{
					string userId = memberUserId.ToString();
					displayName = $"Player_{userId.Substring(Math.Max(0, userId.Length - 8))}";
					GD.Print($"      No Nickname attribute, using fallback: {displayName}");
				}
				
				// Sprawdź czy to właściciel lobby
				var infoOptions = new LobbyDetailsCopyInfoOptions();
				lobbyDetails.CopyInfo(ref infoOptions, out LobbyDetailsInfo? lobbyInfo);
				bool isOwner = lobbyInfo.HasValue && lobbyInfo.Value.LobbyOwnerUserId.ToString() == memberUserId.ToString();
				
				// Sprawdź czy to lokalny gracz
				bool isLocalPlayer = memberUserId.ToString() == localProductUserId.ToString();
				
				// Dodaj do listy
				var memberData = new Godot.Collections.Dictionary
				{
					{ "userId", memberUserId.ToString() },
					{ "displayName", displayName },
					{ "isOwner", isOwner },
					{ "isLocalPlayer", isLocalPlayer }
				};
				
				membersList.Add(memberData);
				
				GD.Print($"    ✅ Added: {displayName} (Owner: {isOwner}, Local: {isLocalPlayer})");
			}
			else
			{
				GD.PrintErr($"  [{i}] Invalid member UserID!");
			}
		}
		
	GD.Print($"👥 Total members added to list: {membersList.Count}");
	
	// Zapisz do cache
	currentLobbyMembers = membersList;
	
	// Wyślij sygnał do UI
	EmitSignal(SignalName.LobbyMembersUpdated, membersList);
	
	// Aktualizuj licznik graczy
	EmitSignal(SignalName.CurrentLobbyInfoUpdated, currentLobbyId, membersList.Count, 4, isLobbyOwner);
}	/// <summary>
	/// Ustawia DisplayName dla lokalnego gracza jako MEMBER ATTRIBUTE
	/// Player A ustawia swoje atrybuty → Player B je odczytuje → wyświetla nick A
	// ============================================
	// NOWE: Bezpośrednie kopiowanie LobbyDetails handle
	// ============================================
	private void CacheCurrentLobbyDetailsHandle(string reason)
	{
		if (string.IsNullOrEmpty(currentLobbyId)) return;
		if (localProductUserId == null || !localProductUserId.IsValid()) return;
		// Pozwól na odświeżenie w określonych przypadkach (update/status/ensure/refresh) – czasem stary handle może nie mieć nowych atrybutów
		bool allowRefresh = reason == "member_update" || reason == "member_status" || reason == "ensure_sync" || reason == "refresh_info" || reason == "status" || reason == "refresh_after_join";
		if (foundLobbyDetails.ContainsKey(currentLobbyId) && foundLobbyDetails[currentLobbyId] != null && !allowRefresh) return;
		// Jeśli odświeżamy – zwolnij poprzedni handle aby uniknąć wycieków
		if (allowRefresh && foundLobbyDetails.ContainsKey(currentLobbyId) && foundLobbyDetails[currentLobbyId] != null)
		{
			foundLobbyDetails[currentLobbyId].Release();
			foundLobbyDetails.Remove(currentLobbyId);
		}
		var copyOpts = new CopyLobbyDetailsHandleOptions()
		{
			LobbyId = currentLobbyId,
			LocalUserId = localProductUserId
		};
		Result r = lobbyInterface.CopyLobbyDetailsHandle(ref copyOpts, out LobbyDetails detailsHandle);
		if (r == Result.Success && detailsHandle != null)
		{
			foundLobbyDetails[currentLobbyId] = detailsHandle;
			GD.Print($"🔒 Cached LobbyDetails handle for lobby {currentLobbyId} (reason={reason})");
		}
		else
		{
			GD.Print($"❌ Failed to copy LobbyDetails handle (reason={reason}): {r}");
		}
	}
}






