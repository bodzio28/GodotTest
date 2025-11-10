# EOS Developer Authentication Tool - Instrukcja

## Krok 1: Uruchom Developer Auth Tool

Przed uruchomieniem gry musisz uruchomić Developer Auth Tool:

### Lokalizacja:
```
d:\Godod_Projects\lobby\Epic_EOS_SDK\SDK\Tools\EOS_DevAuthTool.exe
```

### Jak uruchomić:
1. Przejdź do folderu: `Epic_EOS_SDK\SDK\Tools\`
2. Uruchom: `EOS_DevAuthTool.exe`
3. Narzędzie uruchomi serwer na `localhost:8080`

## Krok 2: Dodaj użytkownika testowego (opcjonalne)

W Developer Auth Tool możesz dodać testowych użytkowników:

1. Kliknij "Add User" w aplikacji DevAuthTool
2. Wpisz nazwę użytkownika (np. "TestUser1")
3. Kliknij "Create"

## Krok 3: Uruchom grę w Godot

Aplikacja automatycznie połączy się z Developer Auth Tool używając:
- **Device ID**: `User_[nazwa_komputera]`
- **Token**: `localhost:8080`

## Ważne informacje:

⚠️ **Developer Auth Tool MUSI być uruchomiony przed startem gry!**

⚠️ **Ten tryb autoryzacji jest TYLKO do testów lokalnych!**

W produkcji użyj:
- Epic Account Services (logowanie przez Epic Games)
- Steam Auth
- Lub innego wspieranego providera

## Troubleshooting:

### Błąd: "AuthInvalidPlatformToken"
- Upewnij się, że DevAuthTool.exe jest uruchomiony
- Sprawdź czy port 8080 nie jest zajęty przez inną aplikację

### Błąd: "Connection refused"
- Sprawdź firewall Windows
- Upewnij się że DevAuthTool działa na localhost:8080
