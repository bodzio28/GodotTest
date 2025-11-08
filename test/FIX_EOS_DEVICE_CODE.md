# Jak naprawić pusty Device Code w EOS SDK

## Problem

SDK zwraca `AuthPinGrantPending` ale **nie** zawiera `pin_grant_info` (pusty `user_code`, `verification_uri`).
To kończy się błędem `AuthUserInterfaceRequired` (1090).

## Przyczyna

Niepoprawna konfiguracja aplikacji w **Epic Games Developer Portal**.

## Rozwiązanie - Krok po kroku

### 1. Sprawdź Brand Settings

1. Wejdź na: https://dev.epicgames.com/portal/
2. Wybierz swoją **Organization** → **Product** → **Application**
3. Po lewej: **Product Settings** → **Brand Settings**
4. Upewnij się że:
   - **Identity Provider** = `Epic Account Services`
   - **Verification URI** jest ustawiony (np. `https://www.epicgames.com/id/login`)
   - **Brand Review Status** = Approved (lub przynajmniej "In Review")

### 2. Sprawdź Client Policy

1. W tym samym Product, idź do: **Product Settings** → **Clients**
2. Znajdź swój Client (ten którego Client ID używasz w `productDetails.cs`)
3. Kliknij na niego → zakładka **Client Policy**
4. **Client Policy Type** musi być: `TrustedServer` lub `Public` (nie `Confidential`)
5. W sekcji **Features**:
   - Zaznacz: `Connect` (Epic Account Services)
   - Zaznacz: `Epic Account Services` checkbox
6. W sekcji **Permissions**:
   - Zaznacz: `Basic Profile`
   - Zaznacz: `Presence`
   - Zaznacz: `Friends`
   - Opcjonalnie: `Country` (jeśli używasz)

### 3. Zweryfikuj SDK Version

- Upewnij się że używasz EOS SDK **1.15+** (plugin używa nowoczesnej wersji)
- Jeśli masz starszą wersję SDK w `addons/epic-online-services-godot/bin/`, zaktualizuj plugin

### 4. Sprawdź Deployment/Sandbox

1. W Developer Portal: **Product Settings** → **Deployments**
2. Upewnij się że Twój Deployment ma:
   - Poprawny **Sandbox ID** (ten sam co w `productDetails.cs`)
   - **Status** = Active
   - Client Policy przypisany do tego Deployment

### 5. Restart całej sesji

Po zmianach w Portal:

1. **Poczekaj 5-10 minut** (zmiany w Portal propagują się z opóźnieniem)
2. Całkowicie zamknij i uruchom ponownie Godot
3. W grze, najpierw kliknij **Delete Persistent Auth** (wyczyść cache)
4. Dopiero potem **Login (Portal)**

## Alternatywne rozwiązanie - Developer Auth Tool

Jeśli Account Portal nie działa, użyj DevAuth (szybsze do testów):

1. Pobierz: https://dev.epicgames.com/docs/services/en-US/DevAuthTool/index.html
2. Uruchom: `./DevAuthTool -credentialname=default -port=6547`
3. W grze kliknij **Login (DevAuth)**
4. Wpisz: URL: `localhost:6547`, Credential: `default`

DevAuth **nie wymaga** Account Portal i zawsze działa lokalnie.

## Diagnostyka w grze

Po naprawieniu konfiguracji w Portal, powinieneś zobaczyć w konsoli:

```
[HAuth] Kod użytkownika: 'ABC123', URI base: 'https://www.epicgames.com/id/login', URI complete: 'https://...'
[HAuth] Finalny URI: 'https://www.epicgames.com/id/login?code=ABC123'
[HAuth] PIN grant pending (pierwsze wywołanie). Otwieram przeglądarkę: https://...
```

Jeśli nadal widzisz puste `''` - **skonfiguruj ponownie Brand Settings i Client Policy**.

## Kontakt z Epic Support

Jeśli po wszystkich krokach nadal nie działa:

1. https://eoshelp.epicgames.com/
2. Załącz:
   - Product ID
   - Client ID
   - Sandbox ID
   - Screenshot z Client Policy
   - Ten log: `[HAuth] pin_grant_info dict: {...}`
