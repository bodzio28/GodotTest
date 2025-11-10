# Test synchronizacji nicków – Instrukcja

## Zmiany wprowadzone (v2 – wersjonowanie + passive pull)

### 1. Wersjonowanie DisplayName
- Dodano pole `localDisplayNameVersion` (int, startuje od 0)
- **Każde** wywołanie `SetDisplayName()` inkrementuje wersję (`localDisplayNameVersion++`)
- Atrybut `DisplayNameVersion` wysyłany razem z `DisplayName` – **wymusza** event nawet jeśli nazwa identyczna
- Callback `OnLobbyMemberUpdateReceived` czyta obie wartości i loguje wersję

### 2. Passive Pull (backup gdy callback się nie odpali)
- **Joiner**: Po dołączeniu (2s opóźnienie) – automatyczny pull atrybutów wszystkich członków
- **Każdy klient**: Po zmianie nicku (2s opóźnienie dla joinerów) – pull atrybutów innych
- Funkcja `PullRemoteMemberAttributes()` iteruje przez cache, czyta atrybuty z `LobbyDetails` i aktualizuje różnice

### 3. Ulepszone logi
- `SetDisplayName` pokazuje `v{version}`
- `OnLobbyMemberUpdateReceived` loguje `v{version}` przy każdej aktualizacji
- `PullRemoteMemberAttributes` pokazuje co znalazł i czy była zmiana

---

## Procedura testowa (2 instancje)

### Przygotowanie
1. Zbuduj projekt: `dotnet build lobby.sln -c Debug`
2. Uruchom **Instancję A** (będzie hostem)
3. Uruchom **Instancję B** (będzie joinerem)

### Test 1: Podstawowa synchronizacja
**A (Host):**
1. Kliknij "Create Lobby" → Zobacz log:
   ```
   🔒 Cached LobbyDetails handle for lobby ... (reason=create)
   📝 Setting display name: Player_kakor (version: 1)
   ✅ Display name set successfully: Player_kakor v1
   ```

**B (Joiner):**
2. Kliknij "Refresh" i "Join" na lobby A → Zobacz logi:
   ```
   🔒 Cached LobbyDetails handle for lobby ... (reason=join)
   📝 Setting display name: Player_nazwa (version: 1)
   ✅ Display name set successfully: Player_nazwa v1
   ⏰ Joiner: passive pull timer triggered
   🔍 Passive pull: checking remote member attributes...
   🔄 Pulled updated name for ...45c: 'Player_45c' → 'Player_kakor' v1
   ✅ Passive pull completed – member list updated
   ```
   
   **OCZEKIWANIE:** Po ~2 sekundach B powinien zobaczyć **poprawny nick hosta** (Player_kakor), nie fallback.

### Test 2: Zmiana nicku hosta
**A (Host):**
3. Wpisz w pole nicku np. `Alice` i kliknij "Ustaw" → Zobacz:
   ```
   🆕 Local display name set to: Alice v2 (changed=True)
   📝 Setting display name: Alice (version: 2)
   ✅ Display name set successfully: Alice v2
   ```

**B (Joiner):**
4. Sprawdź logi – powinien pojawić się **natychmiast** (lub w ciągu 1s) callback:
   ```
   🔔 Lobby member updated in: ..., User: 00022a...45c
   ✏️ Updated DisplayName: Player_kakor → Alice v2
   ```
   
   **OCZEKIWANIE:** Lista członków u B pokazuje `Alice` (nie Player_xxx).

### Test 3: Zmiana nicku joinera
**B (Joiner):**
5. Wpisz `Bob` i kliknij "Ustaw" → Zobacz:
   ```
   🆕 Local display name set to: Bob v2 (changed=True)
   📝 Setting display name: Bob (version: 2)
   ✅ Display name set successfully: Bob v2
   ⏰ Joiner: passive pull timer triggered (2s później)
   ```

**A (Host):**
6. Sprawdź logi – callback:
   ```
   🔔 Lobby member updated in: ..., User: 0002fd...49
   ✏️ Updated DisplayName: Player_d92fcd49 → Bob v2
   ```
   
   **OCZEKIWANIE:** Host widzi `Bob`, nie fallback.

### Test 4: Szybka wielokrotna zmiana (stress test)
**A (Host):**
7. Szybko zmień nick kilka razy: `Alice1` → `Alice2` → `Alice3` → Zobacz:
   ```
   🆕 Local display name set to: Alice1 v3
   🆕 Local display name set to: Alice2 v4
   🆕 Local display name set to: Alice3 v5
   ```

**B (Joiner):**
8. Sprawdź czy logi pokazują **wszystkie** wersje (dzięki wersjonowaniu każda zmiana generuje event):
   ```
   ✏️ Updated DisplayName: Alice → Alice1 v3
   ✏️ Updated DisplayName: Alice1 → Alice2 v4
   ✏️ Updated DisplayName: Alice2 → Alice3 v5
   ```
   
   **OCZEKIWANIE:** Ostateczna widoczna nazwa to `Alice3`.

---

## Co sprawdzać w logach?

### ✅ Sukces (expected)
- `🔒 Cached LobbyDetails handle` – pojawia się przy create/join/update
- `✏️ Updated DisplayName: X → Y v{N}` – callback działa, pokazuje wersję
- `🔄 Pulled updated name` – passive pull znalazł różnicę i zaktualizował
- Brak fallbacków `Player_xxxxx` w UI (oprócz momentu tuż przed pierwszym update)

### ❌ Problem (needs investigation)
- `⚠️ No LobbyDetails in cache` / `❌ Still no LobbyDetails` – handle nie został pobrany
- `ℹ️ DisplayName unchanged but got update event` – event przyszedł, ale wartość identyczna (to OK jeśli wersje różne)
- Fallback `Player_xxx` **utrzymuje się** po >3 sekundach – atrybut nie dotarł ORAZ passive pull nie zadziałał
- Brak logów `🔔 Lobby member updated` po zmianie nicku – EOS nie wysłał eventu (nie powinno się zdarzyć z wersjonowaniem)

---

## Rozwiązywanie problemów

### Fallback nadal widoczny po 3+ sekundach
1. Sprawdź logi passive pull – czy zadziałał? Jeśli nie ma `🔍 Passive pull: checking...` → timer się nie uruchomił.
2. Sprawdź czy `CacheCurrentLobbyDetailsHandle` zadziałał – szukaj `🔒 Cached`.
3. Sprawdź czy atrybut w ogóle jest ustawiony na nadawcy – uruchom `EnsureLocalDisplayNameSynced` ręcznie.

### Callback się nie wywołuje
1. Wersjonowanie powinno wymuszać event – jeśli nadal brak, sprawdź czy `DisplayNameVersion` faktycznie rośnie (logi powinny pokazywać v1, v2, v3...).
2. Ewentualnie timeout EOS/network – spróbuj zwiększyć opóźnienie passive pull z 2s na 5s dla testów.

### Duplikaty członków
1. Sprawdź czy timer w `OnLobbyMemberStatusReceived` (1s join) nie dodaje jeśli już istnieje – kod powinien to sprawdzać.
2. Jeśli duplikaty – dodaj log przed dodaniem członka i zweryfikuj czy `alreadyExists` działa.

---

## Podsumowanie zmian technicznych

| Element | Przed | Po |
|---------|-------|-----|
| Wersjonowanie | Brak | `DisplayNameVersion` int64, auto++ |
| Passive pull | Brak | 2s timer + PullRemoteMemberAttributes() |
| Logi | Nazwa | Nazwa + wersja (vN) |
| Wymuszenie eventu | Tylko zmiana wartości | Zawsze (różna wersja) |
| Joiner pull | Ręczny | Automatyczny po 2s |

---

## Następne kroki jeśli nadal problem
1. Dodaj log raw atrybutów (dump wszystkich kluczy/wartości) w `PullRemoteMemberAttributes`.
2. Zmniejsz opóźnienie passive pull z 2s na 0.5s (dla szybszego testu).
3. Dodaj przycisk "Force Pull" w UI do ręcznego uruchomienia `PullRemoteMemberAttributes()`.
4. Zaimplementuj okresowy pull (co 5s) dla trwałych lobby – obecnie pull tylko przy join/change.

---
_Data: 10-11-2025 – wersja z wersjonowaniem + passive pull_
