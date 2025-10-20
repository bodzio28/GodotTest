using Godot;
using System;
using System.Collections.Generic;

public partial class NetworkManager : Node
{
    [Export] public int Port { get; set; } = 8910;
    [Export] public string Address { get; set; } = "127.0.0.1";
    [Export] public PackedScene PlayerScene { get; set; }

    private readonly Dictionary<long, Node2D> _players = new();

    public override void _Ready()
    {
        // Ensure we have a player scene
        if (PlayerScene == null)
        {
            PlayerScene = GD.Load<PackedScene>("res://scenes/Player.tscn");
        }

        // Add quick test bindings: H to host, J to join
        if (!InputMap.HasAction("host"))
        {
            InputMap.AddAction("host");
            var ev = new InputEventKey { Keycode = Key.H };
            InputMap.ActionAddEvent("host", ev);
        }
        if (!InputMap.HasAction("join"))
        {
            InputMap.AddAction("join");
            var ev = new InputEventKey { Keycode = Key.J };
            InputMap.ActionAddEvent("join", ev);
        }

        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed("host"))
        {
            Host();
        }
        else if (Input.IsActionJustPressed("join"))
        {
            Join();
        }
    }

    public void Host()
    {
        // if (Multiplayer.MultiplayerPeer != null)
        // {
        //     Multiplayer.MultiplayerPeer = null;
        //     GD.Print("Already in a multiplayer session.");
        //     return;
        // }

        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(Port);
        if (err != Error.Ok)
        {
            GD.PushError($"Failed to create server: {err}");
            return;
        }
        Multiplayer.MultiplayerPeer = peer;
        GD.Print($"Hosting on port {Port}");

        // Spawn server's own player (peer id 1)
        SpawnAndBroadcast(Multiplayer.GetUniqueId());
    }

    public void Join()
    {
        // if (Multiplayer.MultiplayerPeer != null)
        // {
        //     GD.Print("Already in a multiplayer session.");
        //     return;
        // }

        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateClient(Address, Port);
        if (err != Error.Ok)
        {
            GD.PushError($"Failed to connect: {err}");
            return;
        }
        Multiplayer.MultiplayerPeer = peer;
        GD.Print($"Joining {Address}:{Port}...");
    }

    private void OnConnectedToServer()
    {
        GD.Print("Connected to server");
    }

    private void OnConnectionFailed()
    {
        GD.PushError("Connection failed");
        Multiplayer.MultiplayerPeer = null;
    }

    private void OnServerDisconnected()
    {
        GD.PushWarning("Disconnected from server");
        CleanupPlayers();
        Multiplayer.MultiplayerPeer = null;
    }

    private void OnPeerConnected(long id)
    {
        GD.Print($"Peer connected: {id}");

        // Only the server orchestrates spawns
        if (Multiplayer.IsServer())
        {
            // Spawn player for the newly connected peer on everyone
            SpawnAndBroadcast(id);

            // Also inform the new peer about already existing players
            foreach (var kv in _players)
            {
                long existingId = kv.Key;
                Vector2 pos = kv.Value.GlobalPosition;
                RpcId(id, nameof(SpawnPlayer), existingId, pos);
            }
        }
    }

    private void OnPeerDisconnected(long id)
    {
        GD.Print($"Peer disconnected: {id}");
        if (Multiplayer.IsServer())
        {
            // Tell everyone to despawn this player
            Rpc(nameof(DespawnPlayer), id);
        }
        // And despawn locally on the server too
        DespawnPlayer(id);
    }

    private void SpawnAndBroadcast(long peerId)
    {
        // Pick a simple spawn position based on peer ID
        Vector2 pos = new(peerId * 50 % 400, peerId * 80 % 400);

        // Tell everyone (including server) to spawn this player
        Rpc(nameof(SpawnPlayer), peerId, pos);
        // Call locally on server as well
        SpawnPlayer(peerId, pos);
    }

    private Color ColorForPeer(long peerId)
    {
        float hue = Mathf.PosMod((float)(peerId * 0.61803398875), 1.0f);
        return Color.FromHsv(hue, 0.85f, 1.0f); // H,S,V
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void SpawnPlayer(long peerId, Vector2 position)
    {
        if (_players.ContainsKey(peerId)) return;

        var inst = PlayerScene.Instantiate<CharacterBody2D>();
        inst.Modulate = new Color(ColorForPeer(peerId));
        inst.Name = $"Player_{peerId}";
        inst.GlobalPosition = position;
        inst.SetMultiplayerAuthority((int)peerId, true);
        AddChild(inst);

        _players[peerId] = inst;

        GD.Print($"Spawned player for peer {peerId} at {position}");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void DespawnPlayer(long peerId)
    {
        if (_players.TryGetValue(peerId, out var node))
        {
            node.QueueFree();
            _players.Remove(peerId);
        }
    }

    private void CleanupPlayers()
    {
        foreach (var node in _players.Values)
            node.QueueFree();
        _players.Clear();
    }
}
