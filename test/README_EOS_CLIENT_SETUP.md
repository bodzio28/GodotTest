# Naprawa błędu: "Client has no application associated" przy logowaniu Account Portal

URL z błędem (skrócony):

```
https://www.epicgames.com/id/error?errorCode=errors.com.epicgames.accountportal.client_has_no_application&client_id=...&scope=basic_profile friends_list presence offline_access openid
```

Ten komunikat oznacza, że używany `client_id` w Twojej grze nie jest prawidłowo powiązany z **Application** (aplikacją) w Dev Portalu lub nie ma dostępu do produktu/deploymentu którego używasz.

## Najczęstsze przyczyny

1. Klient utworzony w Dev Portalu, ale nie powiązany z żadną Application.
2. Używasz `client_id` z innego produktu / organizacji.
3. Deployment (sandbox) nie ma przypisanego dostępu dla tego klienta.
4. Klient nie ma włączonych uprawnień do logowania przez Account Portal (Identity / OAuth).
5. Pomyłka w wartościach `product_id`, `sandbox_id` albo `deployment_id` (mieszanka środowisk).

## Kroki naprawy w Dev Portal (https://dev.epicgames.com/portal)

1. Zaloguj się i wybierz właściwą **Organization**.
2. Wejdź w sekcję **Products** i wybierz swój produkt. Zapisz wartości: `Product ID`, `Sandbox ID`, `Deployment ID` – muszą odpowiadać tym w pliku `productDetails.cs`.
3. Przejdź do zakładki **Clients** (czasem nazwane "Client Credentials"). Jeśli nie masz klienta:
   - Kliknij "Create Client" / "New".
   - Nadaj nazwę. Zapisz wygenerowany `Client ID` oraz `Client Secret`.
4. Otwórz ustawienia tego klienta i sprawdź pole **Application** (albo "Associated Application"). Jeśli jest puste:
   - Przejdź do sekcji **Applications** (lub **Identity** -> **Applications**).
   - Utwórz nową Application (nazwa np. nazwa gry).
   - Wróć do klienta i przypisz tę Application (Save/Update).
5. W Application upewnij się, że masz włączone wymagane **Scopes**:
   - `basic_profile`
   - `friends_list`
   - `presence`
   - (opcjonalnie) `offline_access`, `openid` jeżeli chcesz refresh token / OpenID.
6. W sekcji **Deployment Access** (lub podobnej) dodaj tego klienta do właściwego deploymentu/sandboxa jeśli taka opcja istnieje.
7. Zapisz zmiany. Odczekaj kilka minut jeśli to świeżo utworzone (propagacja może chwilę trwać).
8. W projekcie Godot zaktualizuj wartości w `productDetails.cs` na dokładnie te z Dev Portalu.
9. Usuń ewentualny stary cache: skasuj lokalny folder `user://eosg-cache` (lub użyj przycisku usuwającego persistent auth jeśli wcześniej logowałeś).
10. Uruchom grę ponownie i wykonaj `login_account_portal_async`. Przeglądarka powinna pokazać stronę logowania zamiast błędu.

## Weryfikacja

Po poprawnej konfiguracji:

- W logach pojawi się `Logged into Epic Account Services with Epic Account ID: ...`
- Drugi raz (po restarcie) możesz użyć `login_persistent_auth_async`.

## Dodatkowe wskazówki

- Jeśli zmienisz `client_secret`, musisz ponownie zalogować się przez Portal (stary refresh token przestanie działać).
- Każda zmiana w scopes może wymagać pełnego ponownego loginu (Portal -> zaakceptowanie uprawnień).
- Upewnij się, że nie używasz testowego client ID typu placeholder (np. kopiowanego z dokumentacji).

## Szybki test poprawności w kodzie

Możesz dodać tymczasowo przed wywołaniem `login_account_portal_async`:

```gdscript
if not HPlatform or not HPlatform.client_id:
    push_error("Brak client_id - sprawdź productDetails.cs")
```

lub w C# sprawdzić długość `client_id` (typowo ~32 znaki).

## Jeśli nadal widzisz błąd

Zrób zrzut ekranu ustawień klienta (bez sekretu) i Application oraz wartości z `productDetails.cs`. Sprawdź czy sandbox/deployment jest identyczny. Jeśli różni się tylko `deployment_id`, popraw go – błędne deploymenty często nie rzucają osobnego komunikatu w fazie przekierowania.

---

Dokument utworzony automatycznie aby pomóc naprawić błąd invalidClient dla Epic Account Portal.
