# Użycie pluginu EOS (epic-online-services-godot) z C# (Godot 4 / .NET)

Ten dokument pokazuje jak z poziomu C# korzystać z addonu `epic-online-services-godot`, jak poprawnie zainicjalizować platformę EOS przed logowaniem oraz jak reagować na sygnały. W repo jest gotowa scena demo `Scenes/EOSAuthDemo.tscn` – możesz ją uruchomić jako punkt startowy.

## Skrót kluczowych elementów

- Autoloady rejestrowane przez plugin: `EOSGRuntime`, `HPlatform`, `HAuth`, `HAchievements`, `HFriends`, `HStats`, `HLeaderboards`, `HLobbies`, `HP2P`.
- Dostęp do nich uzyskasz po ścieżce `/root/NazwaAutoload` (np. `/root/HAuth`).
- ZANIM wywołasz jakąkolwiek metodę logowania musisz zainicjalizować platformę przez `HPlatform.setup_eos_async(HCredentials)`.

## Wymagania

- Godot 4.x (Mono / .NET 6) – projekt posiada plik `.csproj` (w repo: `Networking.csproj`).
- Plugin włączony w: Project > Project Settings > Plugins.
- Klasy C# muszą być `partial` i mieć namespace spójny z projektem (w przykładach: `namespace Networking;`).

## Inicjalizacja platformy EOS (krok krytyczny)

1. Umieść w scenie węzeł (np. `ProductDetails`) ze skryptem C# eksportującym dane uwierzytelniające (zobacz `productDetails.cs`).
2. W `_Ready()` swojego kontrolera UI:
   - Pobierz `/root/HPlatform` oraz `/root/HAuth`.
   - Zbuduj obiekt `HCredentials` (instancja skryptu `res://addons/epic-online-services-godot/heos/hcredentials.gd`).
   - Ustaw wymagane pola (product_id, sandbox_id, deployment_id, client_id, client_secret, encryption_key, product_name, product_version).
   - Wywołaj `setup_eos_async(credentials)` i poczekaj na zakończenie (await sygnału `completed`).
3. Dopiero po powodzeniu inicjalizacji włącz przyciski logowania / wywołuj metody `HAuth`.

Jeśli pominiesz ten krok dostaniesz błędy w stylu: `s_authInterface is null` przy próbie logowania.

## Dostęp do autoloadów z C#

```csharp
var hAuth = GetNodeOrNull("/root/HAuth");
var hPlatform = GetNodeOrNull("/root/HPlatform");
if (hAuth == null || hPlatform == null)
{
    GD.PrintErr("Brak autoloadów HAuth / HPlatform – upewnij się, że plugin jest włączony.");
    return;
}
```

## Wywoływanie metod GDScript (async)

```csharp
// Po inicjalizacji platformy:
hAuth.Call("login_account_portal_async");
```

Wiele metod jest asynchronicznych (korzystają z GDScript `await`) i kończą się emisją sygnałów (`logged_in`, `login_error`, `logged_out` itd.) lub ustawieniem właściwości (`product_user_id`, `display_name`).

## Podłączanie sygnałów (Godot 4 C#)

W Godot 4 sygnatura `Connect` używa `Callable`:

```csharp
_hauth.Connect("logged_in",  new Callable(this, nameof(OnLoggedIn)));
_hauth.Connect("login_error", new Callable(this, nameof(OnLoginError)));
_hauth.Connect("logged_out", new Callable(this, nameof(OnLoggedOut)));
```

Metody obsługi muszą akceptować tyle parametrów ile emituje sygnał – w przypadku braku argumentów możesz użyć pustej sygnatury. Jeśli sygnał przekazuje dane (np. kod błędu) dodaj parametr typu `object`.

## Minimalny przykład (wyciąg z `Scripts/EOSDemo.cs`)

```csharp
public partial class EOSDemo : Control
{
    private Node _hauth;

    public override void _Ready()
    {
        _hauth = GetNode("/root/HAuth");
        _hauth.Connect("logged_in", new Callable(this, nameof(OnLoggedIn)));
        _hauth.Connect("login_error", new Callable(this, nameof(OnLoginError)));
        _ = InitializePlatformAsync();
    }

    private async Task InitializePlatformAsync()
    {
        var hPlatform = GetNode("/root/HPlatform");
        var productDetails = GetNodeOrNull<ProductDetails>("ProductDetails");
        var credScript = GD.Load<Script>("res://addons/epic-online-services-godot/heos/hcredentials.gd");
        var creds = (GodotObject)credScript.New();
        creds.Set("product_id", productDetails.product_id);
        // ... ustaw pozostałe pola ...
        var state = (GodotObject)hPlatform.Call("setup_eos_async", creds);
        var result = await ToSignal(state, "completed");
        bool ok = result.Length > 0 && result[0].AsBool();
        if (!ok) GD.PrintErr("EOS init failed");
    }

    public void OnLoginPortalPressed() => _hauth.Call("login_account_portal_async");
    private void OnLoggedIn() => GD.Print("Logged in: " + _hauth.Get("product_user_id"));
    private void OnLoginError(object err) => GD.PrintErr("Login error: " + err);
}
```

## Scena demo

`Scenes/EOSAuthDemo.tscn` zawiera:

- Etykiety statusu i użytkownika.
- Przyciski: Portal Login, Logout, Persistent Auth, Delete Persistent Auth.
- Węzeł `ProductDetails` z eksportowanymi polami credentiali (skrypt `productDetails.cs`).
- Skrypt kontrolera: `Scripts/EOSDemo.cs` realizujący inicjalizację i obsługę sygnałów.

Aby użyć:

1. Uzupełnij poufne wartości w `productDetails.cs` (nie commituj ich publicznie!).
2. Ustaw scenę jako główną (lub otwórz ją ręcznie) i uruchom projekt.
3. Poczekaj na status „EOS Initialized…”, następnie kliknij „Login (Portal)”.

## Właściwości przydatne po zalogowaniu

Z węzła `/root/HAuth` możesz odczytać m.in.:

- `product_user_id`
- `display_name`
- `account_id`
- `login_type`

Przykład:

```csharp
var pid = _hauth.Get("product_user_id") as string;
```

## Bezpieczeństwo credentiali

- Nie commituj prawdziwych wartości `client_secret` ani kluczy szyfrujących do publicznego repozytorium.
- Rozważ wczytywanie danych z zaszyfrowanego pliku / zmiennych środowisk / user data w buildzie.

## Rozszerzanie (kolejne moduły)

Po analogii do `HAuth` możesz podłączać sygnały i wywoływać metody z:

- `HAchievements` – odblokowywanie i pobieranie osiągnięć.
- `HFriends` – lista znajomych.
- `HLeaderboards` – tablice wyników.
- `HLobbies` – lobby / matchmaking.

Każdy autoload ma własne metody async + sygnały zakończenia.

## Debugowanie

- Jeśli sygnały się nie wywołują: sprawdź w Output, czy plugin się poprawnie załadował i czy ścieżka `/root/HAuth` istnieje.
- Jeśli `setup_eos_async` zwraca `false`: zweryfikuj credentiale i uprawnienia aplikacji w portalu EOS.
- Możesz podnieść poziom logowania (jeśli plugin udostępnia taką metodę) – przejrzyj `hplatform.gd`.

## Gdzie dalej

- Źródła pluginu: katalog `addons/epic-online-services-godot/heos`.
- Oficjalna dokumentacja EOS: https://dev.epicgames.com/docs
- Repo pluginu upstream: https://github.com/3ddelano/epic-online-services-godot

---

Aktualna wersja dokumentu uwzględnia scenę demo oraz poprawny sposób łączenia sygnałów w Godot 4.
