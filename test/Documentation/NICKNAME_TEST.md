# Nickname System - Test Instructions

## Implementacja
System nicków używa **EOS MEMBER ATTRIBUTES** do przechowywania i synchronizacji nicków między graczami.

## Jak testować:

### 1. Uruchom dwie instancje gry

### 2. Instancja A (HOST):
1. ✅ Wpisz nick w pole "Twój nick:" (np. "AliceHost")
2. ✅ Kliknij "Ustaw" - powinieneś zobaczyć w logach:
   ```
   ✅ Pending nickname set to: AliceHost
   ```
3. ✅ Kliknij "Utwórz lobby"
4. ✅ Po chwili powinieneś zobaczyć w logach:
   ```
   📝 Setting member attribute: Nickname = 'AliceHost'
   ✅ Member attribute 'Nickname' set successfully: 'AliceHost'
   👥 Getting X lobby members from lobby...
     Member 0: UserID=...
       AttributeCount=1
       Attribute: Nickname = AliceHost
   ```
5. ✅ W UI powinieneś zobaczyć:
   - Twoja nazwa: **AliceHost** (nie Player_xxx!)
   - Pole nicku jest **UKRYTE** (bo jesteś w lobby)

### 3. Instancja B (JOINER):
1. ✅ Wpisz nick w pole "Twój nick:" (np. "BobJoiner")
2. ✅ Kliknij "Ustaw"
3. ✅ Kliknij "Search Lobbies"
4. ✅ Znajdź lobby hosta i kliknij "Join"
5. ✅ Po chwili powinieneś zobaczyć w logach:
   ```
   📝 Setting member attribute: Nickname = 'BobJoiner'
   ✅ Member attribute 'Nickname' set successfully: 'BobJoiner'
   👥 Getting X lobby members from lobby...
     Member 0: UserID=... (Host)
       AttributeCount=1
       Attribute: Nickname = AliceHost
     Member 1: UserID=... (You)
       AttributeCount=1
       Attribute: Nickname = BobJoiner
   ```
6. ✅ W UI powinieneś zobaczyć:
   - Host: **AliceHost**
   - Ty: **BobJoiner**
   - Pole nicku jest **UKRYTE**

### 4. Weryfikacja na hoście (Instancja A):
Po dołączeniu joinera, host powinien automatycznie zobaczyć:
- Ty: **AliceHost**
- Nowy gracz: **BobJoiner**

### 5. Test braku nicku (fallback):
1. ✅ Wyjdź z lobby (kliknij "Opuść lobby")
2. ✅ Pole nicku powinno się **POKAZAĆ** ponownie
3. ✅ NIE wpisuj nicku (zostaw puste)
4. ✅ Dołącz do lobby
5. ✅ Powinieneś zobaczyć fallback: **Player_xxxxxxxx** (ostatnie 8 znaków ProductUserId)

### 6. Test blokady zmiany nicku w lobby:
1. ✅ Gdy jesteś w lobby, pole nicku jest **NIEWIDOCZNE**
2. ✅ Nie można zmienić nicku dopóki nie opuścisz lobby
3. ✅ Po opuszczeniu lobby, pole staje się widoczne ponownie

## Oczekiwane zachowanie:

### ✅ Nick ustawiony PRZED joinowaniem:
- Nick jest wysyłany jako MEMBER attribute przy Create/Join
- Wszyscy w lobby widzą prawdziwy nick (nie Player_xxx)

### ✅ Synchronizacja między klientami:
- Host widzi nicki wszystkich joinerów
- Joiners widzą nick hosta
- Joiners widzą nicki innych joinerów

### ✅ Blokada w lobby:
- Pole nicku ukryte gdy jesteś w lobby
- Pole nicku widoczne gdy nie jesteś w lobby

### ✅ Fallback dla pustego nicku:
- Jeśli nie ustawisz nicku: `Player_xxxxxxxx`
- Jeśli ustawisz nick: Twój nick

## Co sprawdzać w logach:

```
✅ Pending nickname set to: [nick]          ← Ustawienie przed lobby
📝 Setting member attribute: Nickname = '[nick]'  ← Wysłanie do EOS
✅ Member attribute 'Nickname' set successfully   ← Potwierdzenie EOS
👥 Getting X lobby members...              ← Odczyt członków
  Attribute: Nickname = [nick]             ← Nick odczytany z atrybutów
```

## Problemy do sprawdzenia:

❌ **Jeśli nick nie jest widoczny:**
- Sprawdź logi: Czy `SetMemberAttribute` został wywołany?
- Sprawdź logi: Czy `GetLobbyMembers()` widzi `AttributeCount > 0`?
- Sprawdź logi: Czy atrybut ma klucz "Nickname" (nie "DisplayName")?

❌ **Jeśli pole nicku nie znika w lobby:**
- Sprawdź czy sygnały `LobbyJoined`/`LobbyCreated` są emitowane
- Sprawdź czy `LobbyListUI` nasłuchuje tych sygnałów

❌ **Jeśli pole nicku nie wraca po wyjściu:**
- Sprawdź czy sygnał `LobbyLeft` jest emitowany w `OnLeaveLobbyComplete`

## Architektura:

```
pendingNickname (private field)
    ↓ SetPendingNickname() [UI wywołuje]
    ↓
OnCreateLobbyComplete / OnJoinLobbyComplete
    ↓ Timer 0.5s
    ↓ SetMemberAttribute("Nickname", pendingNickname)
    ↓ UpdateLobbyModification + AddMemberAttribute
    ↓ UpdateLobby [wysłanie do EOS]
    ↓
EOS replikuje do wszystkich klientów
    ↓
OnLobbyMemberStatusReceived (JOINED event)
    ↓ Timer 0.5s
    ↓ GetLobbyMembers()
    ↓ LobbyDetails.GetMemberByIndex()
    ↓ LobbyDetails.CopyMemberAttributeByIndex()
    ↓ Sprawdź klucz == "Nickname"
    ↓
EmitSignal(LobbyMembersUpdated) → UI aktualizuje listę
```

## Sanityzacja nicku:
- Min 2 znaki (dopełniane `_`)
- Max 20 znaków (obcięcie)
- Tylko: litery, cyfry, `_`, `-`
- Trim whitespace
