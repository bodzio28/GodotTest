using Godot;
using System;
using System.Threading.Tasks;

namespace Networking;

public partial class EOSDemo : Control
{
    private EOSPlatformAuth _auth = null;
    private Label _statusLabel = null;
    private Label _userLabel = null;
    private bool _eosReady = false;
    private Button _loginBtn = null;
    private Button _logoutBtn = null;
    private Button _persistentBtn = null;
    private Button _deletePersistentBtn = null;
    private Button _devAuthBtn = null; // opcjonalny przycisk w scenie
    private Button _anonBtn = null;    // opcjonalny przycisk w scenie
    private LineEdit _devAuthUrlEdit = null; // opcjonalne pole URL
    private LineEdit _devAuthCredentialEdit = null; // opcjonalne pole credential
    private Label _deviceCodeLabel = null; // pokazuje aktualny PIN/device code
    private Button _openVerificationBtn = null; // otwiera najnowszy verification URL
    private string _lastVerificationUri = string.Empty;

    public override void _Ready()
    {
        // UI references
        _loginBtn = GetNodeOrNull<Button>("LoginButton");
        _logoutBtn = GetNodeOrNull<Button>("LogoutButton");
        _persistentBtn = GetNodeOrNull<Button>("PersistentLoginButton");
        _deletePersistentBtn = GetNodeOrNull<Button>("DeletePersistentButton");
        _statusLabel = GetNodeOrNull<Label>("StatusLabel");
        _userLabel = GetNodeOrNull<Label>("UserLabel");
        // Opcjonalne przyciski (jeśli istnieją w scenie, podłączymy)
        _devAuthBtn = GetNodeOrNull<Button>("DevAuthButton");
        _anonBtn = GetNodeOrNull<Button>("AnonLoginButton");
        _devAuthUrlEdit = GetNodeOrNull<LineEdit>("DevAuthUrlEdit");
        _devAuthCredentialEdit = GetNodeOrNull<LineEdit>("DevAuthCredentialEdit");
        _deviceCodeLabel = GetNodeOrNull<Label>("DeviceCodeLabel");
        _openVerificationBtn = GetNodeOrNull<Button>("OpenVerificationButton");
        if (_deviceCodeLabel != null) _deviceCodeLabel.Text = ""; // startowo pusto
        if (_openVerificationBtn != null) _openVerificationBtn.Disabled = true;
        UpdateStatus("Status: Initializing...");

        // Disable interaction until EOS is initialized
        SetButtonsEnabled(false);

        // Utwórz i dodaj C# wrapper jako child (fasada nad HPlatform/HAuth)
        _auth = new EOSPlatformAuth();
        AddChild(_auth);
        // Zdarzenia z wrappera -> UI
        _auth.LoggedIn += OnLoggedIn;
        _auth.LoggedOut += OnLoggedOut;
        _auth.LoginError += code => OnLoginError(code);
        _auth.UserChanged += (name, pid) => UpdateUser($"User: {name} (PID: {pid})");
        _auth.PersistentAuthVerified += () => GD.Print("[EOSDemo] Persistent auth verified");
        _auth.SdkLog += (cat, msg) => GD.Print($"SDK {cat} | {msg}");
        _auth.DeviceCodeUpdated += (code, uri) =>
        {
            GD.Print($"[EOSDemo] DeviceCodeUpdated otrzymany: code='{code}' uri='{uri}'");
            // Aktualizuj widok kodu i przycisk do otwierania strony
            _lastVerificationUri = uri ?? string.Empty;
            if (_deviceCodeLabel != null)
            {
                var labelText = string.IsNullOrWhiteSpace(code) ? string.Empty : $"Device code: {code}";
                _deviceCodeLabel.Text = labelText;
                GD.Print($"[EOSDemo] Zaktualizowano DeviceCodeLabel.Text na: '{labelText}'");
            }
            else
            {
                GD.PrintErr("[EOSDemo] DeviceCodeLabel jest NULL – sprawdź nazwę węzła w scenie!");
            }
            if (_openVerificationBtn != null)
            {
                _openVerificationBtn.Disabled = string.IsNullOrWhiteSpace(_lastVerificationUri);
            }
            // Wyraźniejszy komunikat statusu
            var note = string.IsNullOrWhiteSpace(code) ? "" : $" Enter code: {code}";
            UpdateStatus($"Status: Enter code in browser.{note}");
            if (!string.IsNullOrWhiteSpace(uri))
                GD.Print($"[EOSDemo] Verification URL: {uri}");
        };

        // Initialize EOS Platform before allowing login
        _ = InitializePlatformAsync();
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _loginBtn?.SetDeferred("disabled", !enabled);
        _logoutBtn?.SetDeferred("disabled", !enabled);
        _persistentBtn?.SetDeferred("disabled", !enabled);
        _deletePersistentBtn?.SetDeferred("disabled", !enabled);
    }

    private async Task InitializePlatformAsync()
    {
        UpdateStatus("Status: Initializing EOS Platform...");

        // Initialize via wrapper
        var pd = GetNodeOrNull<productDetails>("ProductDetails");
        bool ok = await _auth.InitializeAsync(pd);
        if (ok)
        {
            // Ustaw standardowe scope bez Country (który powoduje dodatkowe wymagania). Możesz zmienić w UI później.
            _auth.SetAuthScopes(basicProfile: true, friendsList: true, presence: true, country: false);
        }

        if (!ok)
        {
            UpdateStatus("Status: ERROR - EOS init failed (see logs)");
            GD.PrintErr("EOS initialization failed via HPlatform.setup_eos_async");
            return;
        }

        _eosReady = true;
        SetButtonsEnabled(true);
        UpdateStatus("Status: EOS Initialized - You can login now");
    }

    // UI callbacks (wired in scene)
    public void OnLoginPortalPressed()
    {
        if (!_eosReady)
        {
            UpdateStatus("Status: EOS not ready yet...");
            return;
        }
        // Debounce: wyłącz przycisk aby uniknąć wielokrotnego wywołania otwierającego wiele kart przeglądarki
        _loginBtn?.SetDeferred("disabled", true);
        UpdateStatus("Status: Logging in via Portal...");
        _ = _auth.LoginPortalAsync();
    }

    public void OnLogoutPressed()
    {
        if (!_eosReady)
        {
            UpdateStatus("Status: EOS not ready yet...");
            return;
        }
        UpdateStatus("Status: Logging out...");
        _ = _auth.LogoutAsync();
    }

    public void OnPersistentLoginPressed()
    {
        if (!_eosReady)
        {
            UpdateStatus("Status: EOS not ready yet...");
            return;
        }
        UpdateStatus("Status: Logging in (Persistent Auth)...");
        _ = _auth.LoginPersistentAuthAsync();
    }

    public async void OnDeletePersistentPressed()
    {
        if (!_eosReady)
        {
            UpdateStatus("Status: EOS not ready yet...");
            return;
        }

        GD.Print("[EOSDemo] Użytkownik kliknął Delete Persistent Auth");
        UpdateStatus("Status: Deleting persistent auth...");

        bool success = await _auth.DeletePersistentAuthAsync();

        if (success)
        {
            UpdateStatus("Status: Persistent auth deleted successfully!");
            GD.Print("[EOSDemo] ✓ Persistent auth został pomyślnie usunięty");
        }
        else
        {
            // Jeśli normalne usuwanie nie zadziałało, spróbuj manualnie usunąć cache
            GD.PrintErr("[EOSDemo] Normalne usuwanie nie powiodło się, próbuję manualnego usunięcia cache...");
            UpdateStatus("Status: Trying manual cache deletion...");

            bool manualSuccess = _auth.DeleteCacheDirectoryManual();
            if (manualSuccess)
            {
                UpdateStatus("Status: Cache deleted! RESTART required!");
                GD.Print("[EOSDemo] ✓ Cache został usunięty ręcznie - WYMAGANY RESTART APLIKACJI");
            }
            else
            {
                UpdateStatus("Status: Failed to delete cache (see console)");
                GD.PrintErr("[EOSDemo] ✗ Nie udało się usunąć cache - sprawdź console i usuń ręcznie folder user://eosg-cache");
            }
        }
    }

    // Wrapper event handlers
    private void OnLoggedIn()
    {
        var pid = _auth?.ProductUserId;
        var displayName = _auth?.DisplayName;
        UpdateStatus($"Status: Logged In");
        UpdateUser($"User: {displayName} (PID: {pid})");
        GD.Print($"Logged in. PID={pid}, display_name={displayName}");

        // Avoid confusing flow: PersistentAuth is only for next run; disable button while logged in
        _persistentBtn?.SetDeferred("disabled", true);
        // Re-enable login button now that flow finished
        _loginBtn?.SetDeferred("disabled", false);
        // Opcjonalnie po loginie portalowym można szybko zweryfikować token (nie blokuje UI)
        _ = _auth.VerifyBrowserLoginAsync(5.0);
        // Wyczyść UI kodu
        if (_deviceCodeLabel != null) _deviceCodeLabel.Text = "";
        if (_openVerificationBtn != null) _openVerificationBtn.Disabled = true;
        _lastVerificationUri = string.Empty;
    }

    private void OnLoggedOut()
    {
        UpdateStatus("Status: Logged Out");
        UpdateUser("User: <none>");
        GD.Print("Logged out");

        // Re-enable persistent login button after logout
        _persistentBtn?.SetDeferred("disabled", false);
        _loginBtn?.SetDeferred("disabled", false);
    }

    private void OnLoginError(object result)
    {
        UpdateStatus($"Status: Login Error - {result}");
        GD.PrintErr($"Login error: {result}");
        // W przypadku błędu zezwól ponownie na próbę
        _loginBtn?.SetDeferred("disabled", false);
        // Wyczyść UI kodu
        if (_deviceCodeLabel != null) _deviceCodeLabel.Text = "";
        if (_openVerificationBtn != null) _openVerificationBtn.Disabled = true;
        _lastVerificationUri = string.Empty;
    }

    // Opcjonalne hooki do dodatkowych przycisków, jeśli istnieją
    public void OnDevAuthPressed()
    {
        if (_devAuthBtn == null) return;
        if (!_eosReady) { UpdateStatus("Status: EOS not ready yet..."); return; }
        UpdateStatus("Status: Logging in (DevAuth)...");
        var url = _devAuthUrlEdit != null && !string.IsNullOrWhiteSpace(_devAuthUrlEdit.Text) ? _devAuthUrlEdit.Text.Trim() : "http://localhost:6547";
        var cred = _devAuthCredentialEdit != null && !string.IsNullOrWhiteSpace(_devAuthCredentialEdit.Text) ? _devAuthCredentialEdit.Text.Trim() : "default";
        _ = _auth.LoginDevAuthAsync(url, cred);
    }

    public void OnAnonPressed()
    {
        if (_anonBtn == null) return;
        if (!_eosReady) { UpdateStatus("Status: EOS not ready yet..."); return; }
        UpdateStatus("Status: Logging in (Anonymous DeviceID)...");
        _ = _auth.LoginAnonymousAsync("User");
    }

    public void OnOpenVerificationPressed()
    {
        if (string.IsNullOrWhiteSpace(_lastVerificationUri)) return;
        OS.ShellOpen(_lastVerificationUri);
    }

    private void UpdateStatus(string text)
    {
        if (_statusLabel != null)
            _statusLabel.Text = text;
    }

    private void UpdateUser(string text)
    {
        if (_userLabel != null)
            _userLabel.Text = text;
    }
}
