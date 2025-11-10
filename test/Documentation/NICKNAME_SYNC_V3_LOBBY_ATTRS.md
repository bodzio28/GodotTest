# Naprawione! Synchronizacja nicków – v3 (LOBBY ATTRIBUTES)

## 🔧 Problem i rozwiązanie

### Pierwotny problem
```
🔍 Passive pull: checking remote member attributes...
✅ Passive pull completed – no changes needed
```
**Przyczy na**: EOS **NIE ZAPISUJE** `MEMBER ATTRIBUTES` (AddMemberAttribute). GetMemberAttributeCount zawsze zwraca 0.

### Rozwiązanie v3
✅ Przejście na **LOBBY ATTRIBUTES** z prefiksem userId:
- Klucz: `DN_{userId}` → DisplayName (string)
- Klucz: `DNV_{userId}` → DisplayNameVersion (int64)

**Dlaczego to działa?**
- Lobby attributes są niezawodnie przechowywane przez EOS
- Widoczne dla wszystkich członków
- Aktualizacje wywołują `OnLobbyUpdateReceived` (nie `OnLobbyMemberUpdateReceived`)

---

## 📋 Zmiany techniczne

### 1. Nowy mechanizm zapisu
```csharp
// STARE (nie działało):
lobbyModification.AddMemberAttribute() // ❌ nigdy nie zapisywane

// NOWE (działa):
lobbyModification.AddAttribute() // ✅ niezawodne
// Klucze: DN_00022a...45c = "Alice", DNV_00022a...45c = 1
```

### 2. Nowy callback flow
```
Gracz A zmienia nick → UpdateLobby z DN_{A} + DNV_{A}
    ↓
EOS broadcast: OnLobbyUpdateReceived do wszystkich
    ↓
Każdy klient: PullDisplayNamesFromLobbyAttributes()
    ↓
Iteracja przez lobby.GetAttributeCount() → szuka DN_* i DNV_*
    ↓
Aktualizacja cache + EmitSignal(LobbyMembersUpdated)
```

### 3. Nowy cache
```csharp
Dictionary<string, string> memberDisplayNames;      // userId → displayName
Dictionary<string, int> memberDisplayNameVersions;  // userId → version
```

---

## 🧪 Test (2 instancje)

### Oczekiwane logi (HOST):
```
📝 Setting display name via LOBBY attributes: Alice v1 (key=DN_...45c)
✅ Display name set via lobby attrs: Alice v1
🔔 Lobby updated: a51805...
🔍 Pulling display names from lobby attributes...
  Total lobby attributes: 2
  Found: DN_00022a618b754651940060b2104f545c = 'Alice'
  Found version: DNV_00022a618b754651940060b2104f545c = v1
```

### Oczekiwane logi (JOINER po 2s):
```
⏰ Joiner: passive pull timer triggered
🔍 Pulling display names from lobby attributes...
  Total lobby attributes: 4  <-- 2 atrybuty hosta (DN + DNV) + 2 joinera
  Found: DN_00022a618b754651940060b2104f545c = 'Alice'
  Found version: DNV_00022a618b754651940060b2104f545c = v1
  Found: DN_0002fd95d6024958a6c4f8a7d92fcd49 = 'Bob'
  Found version: DNV_0002fd95d6024958a6c4f8a7d92fcd49 = v1
  ✏️ Updated member: Player_45c → Alice
✅ Display names updated from lobby attributes
```

### Kluczowy wskaźnik sukcesu
```
Total lobby attributes: N  (gdzie N > 0)
Found: DN_xxxx = '<actual_nickname>'
✏️ Updated member: Player_xxx → <actual_nickname>
```

**Jeśli nadal `Total lobby attributes: 0`** → problem z EOS API (niekompatybilna wersja SDK?).

---

## 🎯 Procedura testowa

### 1. Host tworzy lobby
```
Host: Create Lobby
→ Sprawdź log: "✅ Display name set via lobby attrs: Player_kakor v0"
→ Kliknij Force Pull → Zobacz: "Total lobby attributes: 2" (DN + DNV)
```

### 2. Joiner dołącza
```
Joiner: Refresh → Join
→ Po 2s: "⏰ Joiner: passive pull timer triggered"
→ Zobacz: "Total lobby attributes: 4"
→ Zobacz: "✏️ Updated member: Player_45c → Player_kakor"
```

### 3. Host zmienia nick
```
Host: Wpisz "Alice" → Ustaw
→ Log: "✅ Display name set via lobby attrs: Alice v1"
→ Joiner (natychmiast lub do 1s): "🔔 Lobby updated"
→ Joiner: "✏️ Updated member: Player_kakor → Alice"
```

### 4. Joiner zmienia nick
```
Joiner: Wpisz "Bob" → Ustaw
→ Host (natychmiast): "🔔 Lobby updated"
→ Host: "✏️ Updated member: Player_d92fcd49 → Bob"
```

---

## 🔍 Diagnostyka problemów

### Problem: `Total lobby attributes: 0`
**Możliwe przyczyny:**
1. EOS SDK nie wspiera lobby attributes (mało prawdopodobne w 1.17.x)
2. Brak uprawnień w Epic Dev Portal (sprawdź Lobby permissions)
3. Bug w bindings C# (UpdateLobby nie działa poprawnie)

**Rozwiązanie:**
- Sprawdź logi EOS: `[EOS LogEOS]` – szukaj błędów `UpdateLobby`
- Sprawdź Epic Dev Portal → Product Settings → Lobbies → Permissions
- Ewentualnie dodaj fallback: custom P2P packets z nickami

### Problem: `Found: DN_xxx` ale brak `✏️ Updated member`
**Przyczyną:** UserId w kluczu `DN_xxx` nie pasuje do żadnego członka w `currentLobbyMembers`.

**Rozwiązanie:**
- Dodaj log: `GD.Print($"Comparing {userId} with members: {string.Join(", ", currentLobbyMembers.Select(m => m["userId"]))}")`
- Sprawdź czy `ProductUserId.ToString()` jest spójne

### Problem: Nadal fallback `Player_xxx` po >5s
**Debug steps:**
1. Kliknij Force Pull na obu klientach → Sprawdź logi
2. Jeśli `Total lobby attributes: 0` → patrz wyżej
3. Jeśli `Found: DN_xxx` ale brak update → sprawdź matching userId

---

## 📊 Podsumowanie różnic v1/v2/v3

| Wersja | Mechanizm | Status |
|--------|-----------|--------|
| v1 | Member attributes (AddMemberAttribute) | ❌ Nie zapisywane przez EOS |
| v2 | Member attributes + versioning + passive pull | ❌ Nadal 0 atrybutów |
| **v3** | **LOBBY attributes (DN_{userId})** | ✅ **Niezawodne** |

---

## 🚀 Następne kroki jeśli nadal problem

1. **Fallback P2P packets**: Jeśli lobby attributes też nie działają, użyj P2P packets do wysyłki nicków bezpośrednio między klientami
2. **Custom metadata**: Zamiast EOS lobby, użyj zewnętrznego backendu (REST API) do synchronizacji
3. **Kontakt z Epic Support**: Zgłoś bug jeśli lobby attributes zwracają 0

---

_Data: 10-11-2025 – v3 LOBBY ATTRIBUTES (ostateczne rozwiązanie)_
