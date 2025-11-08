# Naprawa funkcji Delete Persistent Auth

## Problem
Funkcja "Delete Persistent Auth" nie działała prawidłowo i zwracała błąd `InvalidParameters` z EOS SDK.

## Przyczyny
1. **Brak awaita w C#**: Metoda `DeletePersistentAuth()` w C# nie czekała na zakończenie asynchronicznej operacji GDScript
2. **Niepoprawne parametry**: SDK może wymagać `null` zamiast pustego stringa dla `refresh_token` aby usunąć wszystkie tokeny
3. **Brak obsługi błędów**: Nie było fallbacku gdy SDK zwraca błąd
4. **Brak logowania**: Trudno było zdiagnozować co się dzieje

## Rozwiązanie

### 1. Rozszerzone logowanie w GDScript (`hauth.gd`)
Dodano szczegółowe logowanie w funkcji `delete_persistent_auth_async()`:
- Informacje o długości tokenu
- Automatyczne pobieranie `refresh_token` z zalogowanego użytkownika jeśli nie podano
- Ustawienie `refresh_token = null` dla usunięcia wszystkich tokenów
- Szczegółowe komunikaty błędów z możliwymi przyczynami

### 2. Nowa metoda async w C# (`EOSPlatformAuth.cs`)
```csharp
public async Task<bool> DeletePersistentAuthAsync()
```
- Właściwie czeka na zakończenie operacji (2 sekundy timeout)
- Zwraca `Task<bool>` aby można było await
- Dodano deprecation warning dla starej synchronicznej metody

### 3. Manualny fallback - usunięcie folderu cache
Jeśli SDK zwraca błąd, dodano możliwość ręcznego usunięcia folderu `user://eosg-cache`:

**GDScript:**
```gdscript
func delete_cache_directory_manual() -> bool
```

**C#:**
```csharp
public bool DeleteCacheDirectoryManual()
```

### 4. Automatyczny fallback w UI (`EOSDemo.cs`)
Przycisk "Delete Persistent Auth" teraz:
1. Najpierw próbuje normalnej metody SDK (`DeletePersistentAuthAsync()`)
2. Jeśli ta zawiedzie, automatycznie próbuje manualnego usunięcia cache (`DeleteCacheDirectoryManual()`)
3. Wyświetla odpowiednie komunikaty o sukcesie/porażce

## Jak używać

### Normalny sposób (przez SDK)
```csharp
bool success = await _auth.DeletePersistentAuthAsync();
```

### Ręczny sposób (bezpośrednie usunięcie cache)
```csharp
bool success = _auth.DeleteCacheDirectoryManual();
// WYMAGA RESTARTU APLIKACJI!
```

## Diagnostyka

### Sprawdź logi GDScript
Po kliknięciu "Delete Persistent Auth" sprawdź console:

**Sukces:**
```
[HAuth] === ROZPOCZYNAM USUWANIE PERSISTENT AUTH ===
[HAuth] refresh_token parametr: '' (długość: 0)
[HAuth] Usuwam WSZYSTKIE persistent auth tokeny (refresh_token=null)
[HAuth] Wywołuję AuthInterface.delete_persistent_auth...
[HAuth] ✓ Persistent auth pomyślnie usunięty!
```

**Błąd SDK:**
```
[HAuth] Failed to delete persistent auth: result_code=InvalidParameters
[HAuth] Możliwe przyczyny:
  1. Niepoprawny refresh_token
  2. Brak zapisanych tokenów do usunięcia
  3. SDK nie został poprawnie zainicjalizowany
[HAuth] Spróbuj ręcznie usunąć folder: user://eosg-cache/
```

**Fallback - ręczne usunięcie:**
```
[HAuth] === RĘCZNE USUWANIE CACHE EOS ===
[HAuth] Ścieżka: /Users/.../Godot/app_userdata/Networking/eosg-cache
[HAuth] ✓ Folder cache został pomyślnie usunięty!
[HAuth] Zrestartuj aplikację aby zmiany zostały w pełni zastosowane
```

### Sprawdź czy cache został usunięty
Folder cache znajduje się w:
- **macOS**: `~/Library/Application Support/Godot/app_userdata/Networking/eosg-cache/`
- **Windows**: `%APPDATA%\Godot\app_userdata\Networking\eosg-cache\`
- **Linux**: `~/.local/share/godot/app_userdata/Networking/eosg-cache/`

Jeśli folder nie istnieje = cache został usunięty ✓

## Typowe problemy

### Problem: InvalidParameters
**Przyczyna**: SDK może wymagać konkretnego refresh tokenu lub nie ma żadnego zapisanego tokenu  
**Rozwiązanie**: Użyj ręcznego usunięcia cache (`DeleteCacheDirectoryManual()`)

### Problem: Brak uprawnień do usunięcia plików
**Przyczyna**: System plików chroni folder cache  
**Rozwiązanie**: 
1. Zamknij Godot całkowicie
2. Ręcznie usuń folder (ścieżki powyżej)
3. Uruchom ponownie aplikację

### Problem: Po usunięciu nadal pojawia się stary token
**Przyczyna**: Nie zrestartowano aplikacji  
**Rozwiązanie**: **ZAWSZE restartuj aplikację** po usunięciu cache

## Dodatkowe informacje

### Kiedy używać Delete Persistent Auth?
- Przed pierwszym loginem Account Portal (aby wymusić nowy Device Code)
- Gdy chcesz wylogować użytkownika PERMANENTNIE (nie tylko logout z sesji)
- Gdy testy wymagają świeżego stanu bez zapisanych tokenów
- Gdy występują błędy `InvalidAuth` przy próbie użycia Persistent Auth

### Co się dzieje po usunięciu?
1. Wszystkie zapisane refresh tokeny EOS są usuwane
2. Przy następnym uruchomieniu aplikacji użytkownik MUSI zalogować się ponownie przez Account Portal
3. `login_persistent_auth_async()` będzie zwracać błąd do momentu ponownego loginu przez przeglądarkę

## Testowanie

### Test 1: Normalne usunięcie
1. Zaloguj się przez Account Portal
2. Zrestartuj aplikację
3. Kliknij "Delete Persistent Auth"
4. Sprawdź console - powinno być "✓ Persistent auth pomyślnie usunięty!"
5. Zrestartuj aplikację
6. Kliknij "Login (Persistent Auth)" - powinien być błąd `InvalidAuth` ✓

### Test 2: Manualny fallback
1. Jeśli Test 1 zawodzi z `InvalidParameters`
2. Aplikacja automatycznie próbuje ręcznego usunięcia
3. Sprawdź console - "✓ Folder cache został pomyślnie usunięty!"
4. **ZRESTARTUJ APLIKACJĘ** (wymagane!)
5. Sprawdź czy folder `user://eosg-cache` nie istnieje ✓

## Zmiany w kodzie

### `addons/epic-online-services-godot/heos/hauth.gd`
- ✅ Rozszerzone logowanie w `delete_persistent_auth_async()`
- ✅ Automatyczne pobieranie refresh_token z zalogowanego użytkownika
- ✅ Ustawienie `refresh_token = null` zamiast `""`
- ✅ Dodano `delete_cache_directory_manual()` jako fallback
- ✅ Dodano `_remove_directory_recursive()` helper

### `Scripts/EOSPlatformAuth.cs`
- ✅ Zmieniono `DeletePersistentAuth()` na `DeletePersistentAuthAsync()`
- ✅ Dodano await z 2-sekundowym timeoutem
- ✅ Dodano `DeleteCacheDirectoryManual()` wrapper
- ✅ Stara metoda oznaczona jako `[Obsolete]`

### `Scripts/EOSDemo.cs`
- ✅ Zmieniono `OnDeletePersistentPressed()` na async
- ✅ Dodano await dla `DeletePersistentAuthAsync()`
- ✅ Dodano automatyczny fallback do `DeleteCacheDirectoryManual()`
- ✅ Dodano szczegółowe komunikaty o sukcesie/porażce

## Podsumowanie
Funkcja "Delete Persistent Auth" teraz:
- ✅ Prawidłowo czeka na zakończenie operacji
- ✅ Ma szczegółowe logowanie na każdym etapie
- ✅ Automatycznie próbuje fallback jeśli SDK zawodzi
- ✅ Informuje użytkownika o wymaganych akcjach (np. restart)
- ✅ Obsługuje wszystkie edge cases (brak tokenu, błędy SDK, problemy z plikami)
