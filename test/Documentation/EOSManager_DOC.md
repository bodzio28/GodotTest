# EOSManager – Dokumentacja Tymczasowa

> Wersja: tymczasowa (09-11-2025) – przeznaczona do szybkiego onboardingu i debugowania. Ten plik opisuje aktualny stan implementacji w `EOSManager.cs`.

## 1. Cel klasy
`EOSManager` kapsułkuje integrację z Epic Online Services (EOS) dla trybu lobby przy użyciu identyfikacji Device ID (Connect) bez logowania na konto Epic. Zapewnia:
- Inicjalizację SDK i platformy.
- Logowanie P2P (DeviceID).
- Tworzenie / wyszukiwanie / dołączanie / opuszczanie lobby.
- Buforowanie informacji o lobby (`LobbyDetails`).
- Reaktywne aktualizacje listy członków na podstawie callbacków (status + member update).
- Ustawianie i synchronizację atrybutu członka `DisplayName`.

## 2. Główne interfejsy EOS używane
- `PlatformInterface` – baza SDK (Tick w `_Process`).
- `ConnectInterface` – logowanie przez DeviceID (anonimowe P2P).
- `LobbyInterface` – system lobby (Create/Join/Update/Notifications).

## 3. Kluczowe ID i nazwy
- `ProductUserId` (localProductUserId) – używany do lobby i P2P (najważniejszy w tej implementacji).
- `EpicAccountId` – używany tylko przy logowaniu przez Auth (tu opcjonalny / nieaktywny w przepływach DeviceID).
- `localDisplayName` – aktualny lokalny nick gracza; utrzymywany w pamięci + wysyłany jako member attribute.

## 4. Przepływ inicjalizacji
1. `PlatformInterface.Initialize` + utworzenie platformy (`PlatformInterface.Create`).
2. Pobranie `ConnectInterface` i `LobbyInterface`.
3. Rejestracja callbacków: update lobby, update member, status member.
4. Logowanie przez `CreateDeviceId` i `ConnectInterface.Login` (token null + typ `DeviceidAccessToken`).
5. Ustawienie wstępnego `localDisplayName = Player_{UserName}`.

## 5. Tworzenie lobby
- Metoda: `CreateLobby(string lobbyName, uint maxPlayers, bool isPublic)`.
- Wysyła `CreateLobbyOptions`; po sukcesie:
  * Ustawia `currentLobbyId`, `isLobbyOwner = true`.
  * Natychmiast: `CacheCurrentLobbyDetailsHandle("create")` – pobiera żywy handle bez `SearchLobbies()`.
  * Ustawia atrybut członka `DisplayName` przez `SetLocalMemberDisplayName()` (UpdateLobbyModification + AddMemberAttribute).
  * Wysyła sygnały UI: `LobbyCreated`, `CurrentLobbyInfoUpdated`, `LobbyMembersUpdated` (wstępny cache z jednym członkiem).

## 6. Wyszukiwanie lobby
- `SearchLobbies()` tworzy `LobbySearch`, filtruje po `bucket=DefaultBucket`.
- Dla każdego wyniku: kopiuje `LobbyDetails` (UWAGA: wyniki wyszukiwania czasem mają puste/niepełne userID członków – dlatego nie polegamy na nich do pobierania listy członków w czasie rzeczywistym).
- Służy głównie do listy dostępnych lobby i aktualizacji liczby graczy.

## 7. Dołączanie do lobby
- `JoinLobby(lobbyId)` wymaga wcześniej pobranego `LobbyDetails` z wyszukiwania (lub można rozszerzyć o tryb bez search – TODO: ewentualny bezpośredni join po ID gdy mamy handle).
- Po sukcesie: `currentLobbyId`, `isLobbyOwner=false`, `CacheCurrentLobbyDetailsHandle("join")` (lokalny handle), atrybut `DisplayName`.
- Wysyłane sygnały jak przy tworzeniu (wstępna tymczasowa lista z jednym graczem).

## 8. Opuszczanie lobby
- `LeaveLobby()` -> `LeaveLobbyOptions` -> czyszczenie: `currentLobbyId`, `isLobbyOwner`, `currentLobbyMembers`, zatrzymanie timera (jeśli był), zwolnienie stanu.

## 9. Buforowanie LobbyDetails
- Słownik: `foundLobbyDetails[lobbyId] = LobbyDetails`.
- NOWA metoda: `CacheCurrentLobbyDetailsHandle(reason)` używa `CopyLobbyDetailsHandleOptions(LobbyId, LocalUserId)` żeby pobrać aktualny handle bez wykonywania wyszukiwania.
- Powody odświeżenia: `create`, `join`, `member_update`, `status`, `ensure_sync`, `refresh_info`.
- Przy odświeżeniu dla dynamicznych powodów zwalnia stary handle (Release) by uniknąć wycieków.

## 10. Lista członków lobby (cache)
- `currentLobbyMembers`: `Array<Dictionary>` gdzie każdy element zawiera:
  * `userId`: string (ProductUserId.ToString())
  * `displayName`: aktualny znany nick lub fallback `Player_<suffix>`
  * `isOwner`: bool
  * `isLocalPlayer`: bool
- Aktualizacje wyłącznie poprzez callbacki + logikę timers (JOINED 1s później doprecyzowuje nick).

## 11. Callbacki i ich rola
| Callback | Metoda | Cel |
|----------|--------|-----|
| Lobby update | `OnLobbyUpdateReceived` | ogólne zmiany – odświeżenie liczby graczy (`RefreshCurrentLobbyInfo`). |
| Member update | `OnLobbyMemberUpdateReceived` | modyfikacja atrybutów członka (DisplayName). Pobiera z LobbyDetails attributes dla `TargetUserId`. Dodaje nowego członka jeśli nie ma na liście. |
| Member status | `OnLobbyMemberStatusReceived` | JOINED/LEFT. Dodanie/Usunięcie członka + 1s timer doprecyzowujący `DisplayName` po uzyskaniu handle. Host broadcastuje swój `DisplayName` do nowego joinera. |

## 12. Ustawianie nicku (DisplayName)
- Publiczna metoda: `SetDisplayName(newName)`
  * Sanitizacja (długość, znaki, fallback).
  * Aktualizacja lokalnego cache członka natychmiast.
  * Wywołuje `SetLocalMemberDisplayName()` (zapis atrybutu przez UpdateLobby).
  * Jeśli wartość się zmieniła: po 1s `EnsureLocalDisplayNameSynced()`.
- `SetLocalMemberDisplayName()` tworzy modyfikację lobby i dodaje member attribute `DisplayName` (visibility Public), potem `UpdateLobby`.

## 13. Synchronizacja DisplayName – obecny stan
Problem: Drugi klient nie widział zmiany nicku hosta albo dostawał fallback. Źródła:
1. Race condition – pierwszy callback przychodził przed zdobyciem pełnego `LobbyDetails`.
2. Używanie wyników `SearchLobbies()` (częściowe dane członków).
3. Wielokrotne broadcasty z identyczną wartością mogły nie generować pełnego propagation w sieci (potencjalny brak event jeśli wartość się nie zmienia w warstwie transportu).

Aktualne rozwiązania:
- Bezpośrednie `CopyLobbyDetailsHandle` przy create/join/update/status.
- Retry lokalny (`EnsureLocalDisplayNameSynced`) jeśli handle jeszcze nie zwrócił atrybutu.
- Host automatycznie wysyła swój `DisplayName` na join nowego gracza.

Możliwe dalsze ulepszenia (TODO):
- Dodanie atrybutu `DisplayNameVersion` (inkrementacja przy każdej zmianie nicku) – wymusza różnicę i pewny callback.
- Dodatkowy mechanizm pull na joinerze po 2s: enumeracja atrybutów hosta jeśli brak update.
- Weryfikacja czy EOS wymaga unikalnej pary (Key, Value) dla generowania eventu – jeśli tak, wersjonowanie jest konieczne.

## 14. Testowanie synchronizacji nicku (2 instancje)
1. Uruchom instancję A (host). Powstanie log: `🔒 Cached LobbyDetails handle (reason=create)`.
2. A zmienia nick na np. `AAA1`. Sprawdź: `✅ Display name set successfully: AAA1`.
3. Uruchom instancję B (joiner) i dołącz do lobby. Log: `🔒 Cached LobbyDetails handle (reason=join)`.
4. W logu B zobacz czy `OnLobbyMemberUpdateReceived` dla hosta zawiera: `📝 Found DisplayName from LobbyDetails: AAA1` albo `✏️ Updated DisplayName`.
5. Na A zmień nick kilka razy: `AAA2`, `AAA3` – obserwuj czy B dostaje kolejne aktualizacje.
6. Jeśli B nie aktualizuje się: sprawdź czy pojawia się `member_update` w ogóle. Jeśli jest event bez zmiany – rozważ wdrożenie `DisplayNameVersion`.
7. Opcjonalnie zrób sztuczne opóźnienie (sleep) przed `SetLocalMemberDisplayName` – potwierdzisz czy race condition był źródłem problemu.

## 15. Emisje sygnałów do UI
- `LobbyListUpdated` – lista lobby (wynik search).
- `LobbyCreated`, `LobbyJoined` – wejście do lobby.
- `CurrentLobbyInfoUpdated` – liczba graczy + właściciel.
- `LobbyMembersUpdated` – cała lista członków (każda zmiana nicku, join, leave, uzupełnienie fallbacku).

## 16. Typowe pułapki / błędy
| Sytuacja | Objaw | Rozwiązanie |
|----------|-------|-------------|
| Brak handle w member update | Log: "No LobbyDetails in cache" | Teraz automatyczny `CacheCurrentLobbyDetailsHandle("member_update")`. |
| Fallback zamiast nicku | `Player_xxxxx` pojawia się | Atrybut jeszcze nie dotarł – patrz retry + ewentualnie wersjonowanie. |
| Duplikaty członków | Licznik zawyżony | Timer join + sprawdzenie istnienia (już wdrożone). |
| Brak aktualizacji przy tej samej wartości | Nick nie zmienia się u innych | Wdrożyć `DisplayNameVersion` (TODO). |

## 17. Pomysł na przyszłe zmiany (Backlog)
- Atrybut `DisplayNameVersion` (int, auto++). Klient porównuje max wersji i aktualizuje.
- Kompozytowy atrybut zbiorczy (JSON) dla wielu danych gracza (DisplayName, Level, Skin) – redukuje liczbę UpdateLobby.
- Ograniczenie częstotliwości broadcastu hosta (debounce 300–500 ms przy spamie zmian nicku).
- Zewnętrzny moduł testów automatycznych (mały harness w Godot do symulacji wielu instancji).

## 18. Szybki pseudokod przepływu zmiany nicku
```
Player clicks "Ustaw" -> SetDisplayName(new)
  sanitize & assign localDisplayName
  update local cache member.displayName
  emit LobbyMembersUpdated
  SetLocalMemberDisplayName() -> UpdateLobby (member attribute DisplayName)
  timer 1s -> EnsureLocalDisplayNameSynced()
    if attribute missing or mismatch -> SetLocalMemberDisplayName() retry
```

## 19. Debug checklist przed zgłoszeniem błędu
- Czy pojawił się `🔒 Cached LobbyDetails handle (reason=...)` w logu danej instancji?
- Czy `OnLobbyMemberUpdateReceived` jest wywoływane po zmianie nicku?
- Czy w atrybutach hosta jest już `DisplayName` (sprawdź enumerację attrCount)?
- Czy nick naprawdę się zmienił (różna wartość)? Jeśli nie – test z inną wartością.
- Czy brak eventu występuje tylko gdy wartość identyczna? Jeśli tak → wdrożyć wersjonowanie.

## 20. FAQ (skrót)
**P: Dlaczego w ogóle nie używamy GetLobbyMembers() z wyników search?**  
Bo wyniki `LobbySearch` mogą mieć niepełne lub puste `UserID` członków (zachowanie EOS). Lepiej użyć żywego handle z `CopyLobbyDetailsHandle`.

**P: Czy trzeba wołać SearchLobbies() po każdej zmianie?**  
Nie, tylko gdy potrzebna lista lobby do UI. Lokalne odświeżenia atrybutów i statusu obsługują callbacki.

**P: Co jeśli LobbyDetails handle stanie się nieaktualny?**  
Mechanizmy refresh poprzez `CacheCurrentLobbyDetailsHandle` dla powodów dynamicznych zwalniają stary handle i pobierają nowy.

---
_Jeśli potrzebne jest rozwinięcie którejś sekcji lub implementacja wersjonowania – dopisz w TODO._
