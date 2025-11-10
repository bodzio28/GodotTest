# Fix: Empty UserID in GetLobbyMembers after Join

## Problem diagnosed from logs:

```
👥 Getting 1 lobby members from lobby 004d2711a25844bbb07008f069608615...
  Member 0: UserID=
  ERROR:   [0] Invalid member UserID!
```

**Root cause:** `GetMemberByIndex()` returns **empty/invalid ProductUserId** because the joiner was using `LobbyDetails` handle from the **initial join**, which doesn't have complete member data yet.

## Why this happened:

1. **Joiner joins lobby** → `OnJoinLobbyComplete` → `CacheCurrentLobbyDetailsHandle("join")`
2. **At this moment**, EOS hasn't fully synchronized member data yet
3. **SearchLobbiesAndRefresh()** is called → tries to use the incomplete LobbyDetails handle
4. **GetMemberByIndex()** returns empty UserID → crash/empty member list

## Solution implemented:

### 1. Force refresh LobbyDetails handle in SearchLobbiesAndRefresh()
```csharp
private void SearchLobbiesAndRefresh()
{
    // FORCE refresh of LobbyDetails handle (with allowRefresh=true)
    GetTree().CreateTimer(0.3).Timeout += () => 
    {
        CacheCurrentLobbyDetailsHandle("refresh_after_join"); // ← NEW reason
        
        // Another delay to ensure handle is up-to-date
        GetTree().CreateTimer(0.3).Timeout += () => 
        {
            RefreshCurrentLobbyInfo();
            GetLobbyMembers(); // ← Now uses fresh handle with complete data
        };
    };
}
```

### 2. Add "refresh_after_join" to allowRefresh list
```csharp
bool allowRefresh = reason == "member_update" 
    || reason == "member_status" 
    || reason == "ensure_sync" 
    || reason == "refresh_info" 
    || reason == "status" 
    || reason == "refresh_after_join"; // ← ADDED
```

### 3. Implement OnLobbyMemberUpdateReceived callback
```csharp
private void OnLobbyMemberUpdateReceived(ref LobbyMemberUpdateReceivedCallbackInfo data)
{
    // When member attributes change (nickname set), refresh member list
    CacheCurrentLobbyDetailsHandle("member_update");
    GetTree().CreateTimer(0.5).Timeout += () =>
    {
        GetLobbyMembers(); // ← All players see updated nicknames
    };
}
```

### 4. Increased timers for nickname synchronization
Changed from 0.5s → 1.0s to give EOS more time to propagate member attributes:
- `OnCreateLobbyComplete`: 1.0s before SetMemberAttribute, then 1.0s before GetLobbyMembers
- `OnJoinLobbyComplete`: 1.0s before SetMemberAttribute

## How it works now:

### Host creates lobby:
1. ✅ `OnCreateLobbyComplete` → `CacheCurrentLobbyDetailsHandle("create")`
2. ✅ Timer 1.0s → `SetMemberAttribute("Nickname", "kakor")`
3. ✅ Timer 1.0s → `GetLobbyMembers()` → Reads own nickname

### Joiner joins lobby:
1. ✅ `OnJoinLobbyComplete` → `CacheCurrentLobbyDetailsHandle("join")` (incomplete data)
2. ✅ `CallDeferred(SearchLobbiesAndRefresh)`
3. ✅ Timer 0.3s → **Force refresh**: `CacheCurrentLobbyDetailsHandle("refresh_after_join")` (complete data!)
4. ✅ Timer 0.3s → `GetLobbyMembers()` → **Now has valid UserIDs!** → Reads host's nickname
5. ✅ Timer 1.0s → `SetMemberAttribute("Nickname", "kakor")`

### Host receives joiner:
1. ✅ `OnLobbyMemberStatusReceived(JOINED)` → `CacheCurrentLobbyDetailsHandle("member_status")`
2. ✅ Timer 0.5s → `GetLobbyMembers()` → Sees joiner (but no nickname yet)
3. ✅ `OnLobbyMemberUpdateReceived` (joiner set nickname) → refresh handle
4. ✅ Timer 0.5s → `GetLobbyMembers()` → **Now sees joiner's nickname!**

## Testing checklist:

- [x] Joiner sees host in member list (not empty)
- [x] Joiner sees host's nickname (not Player_xxx fallback)
- [x] Host sees joiner in member list
- [x] Host sees joiner's nickname after it's set
- [x] Player count shows correctly (2/4, not 0/4)

## Key insight:

**CopyLobbyDetailsHandle() immediately after join does NOT give complete member data.**

You must either:
- Wait longer (1-2 seconds) before first handle copy
- OR force refresh the handle after initial join (our solution)

The `allowRefresh` flag prevents unnecessary handle refreshes (memory leaks), but certain situations (join, member update) **require** a fresh handle to get updated data from EOS backend.
