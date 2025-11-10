using Godot;
using System;

public partial class LobbyListUI : VBoxContainer
{
	private EOSManager eosManager;
	
	// UI dla nicku
	private HBoxContainer nicknameContainer;
	private LineEdit nicknameEdit;
	private Button setNicknameButton;

	// Scena dla pojedynczego elementu lobby (utworzymy ją programatically)
	private PackedScene lobbyItemScene;

	public override void _Ready()
	{
		eosManager = GetNode<EOSManager>("/root/EOSManager");
		
		// Utwórz UI dla nicku (na górze listy)
		CreateNicknameUI();
		
		// Podłącz sygnały z EOSManager
		eosManager.LobbyListUpdated += OnLobbyListUpdated;
		eosManager.LobbyJoined += OnLobbyJoined;
		eosManager.LobbyCreated += OnLobbyCreated;
		eosManager.LobbyLeft += OnLobbyLeft;

		GD.Print("LobbyListUI ready and listening for lobby updates");
	}
	
	private void CreateNicknameUI()
	{
		nicknameContainer = new HBoxContainer();
		
		var nickLabel = new Label();
		nickLabel.Text = "Twój nick:";
		nickLabel.CustomMinimumSize = new Vector2(80, 0);
		nicknameContainer.AddChild(nickLabel);
		
		nicknameEdit = new LineEdit();
		nicknameEdit.PlaceholderText = "Wpisz nick (opcjonalnie)";
		nicknameEdit.CustomMinimumSize = new Vector2(200, 0);
		nicknameEdit.MaxLength = 20;
		nicknameContainer.AddChild(nicknameEdit);
		
		setNicknameButton = new Button();
		setNicknameButton.Text = "Ustaw";
		setNicknameButton.Pressed += OnSetNicknamePressed;
		nicknameContainer.AddChild(setNicknameButton);
		
		// Dodaj na początek (przed listą lobby)
		AddChild(nicknameContainer);
		MoveChild(nicknameContainer, 0);
		
		// Dodaj separator
		var nickSeparator = new HSeparator();
		AddChild(nickSeparator);
		MoveChild(nickSeparator, 1);
	}
	
	private void OnSetNicknamePressed()
	{
		string nickname = nicknameEdit.Text.Trim();
		eosManager.SetPendingNickname(nickname);
		GD.Print($"✅ Nickname set: {nickname}");
	}
	
	private void OnLobbyJoined(string lobbyId)
	{
		// Ukryj UI nicku gdy jesteśmy w lobby (sprawdź czy nie disposed)
		if (nicknameContainer != null && IsInstanceValid(nicknameContainer))
		{
			nicknameContainer.Visible = false;
		}
	}
	
	private void OnLobbyCreated(string lobbyId)
	{
		// Ukryj UI nicku gdy jesteśmy w lobby (sprawdź czy nie disposed)
		if (nicknameContainer != null && IsInstanceValid(nicknameContainer))
		{
			nicknameContainer.Visible = false;
		}
	}
	
	private void OnLobbyLeft()
	{
		// Pokaż UI nicku gdy opuściliśmy lobby (sprawdź czy nie disposed)
		if (nicknameContainer != null && IsInstanceValid(nicknameContainer))
		{
			nicknameContainer.Visible = true;
			GD.Print("✨ Nickname UI shown after leaving lobby! >w<");
		}
		else
		{
			// Safety: Jeśli nickname UI zostało usunięte, stwórz je ponownie! OwO
			GD.Print("⚠️ Nickname UI missing, recreating...");
			CreateNicknameUI();
		}
	}

	private void OnLobbyListUpdated(Godot.Collections.Array<Godot.Collections.Dictionary> lobbies)
	{
		GD.Print($"Updating lobby list UI with {lobbies.Count} lobbies");

		// Wyczyść obecną listę
		ClearLobbyList();

		// Dodaj każde lobby do listy
		foreach (var lobbyData in lobbies)
		{
			AddLobbyItem(lobbyData);
		}
	}

	private void ClearLobbyList()
	{
		// Usuń wszystkie dzieci OPRÓCZ nickname UI (pierwsze 2 elementy: container + separator) ^w^
		var children = GetChildren();
		
		// Zaczynamy od indeksu 2 (pomijamy nicknameContainer i separator)
		for (int i = 2; i < children.Count; i++)
		{
			children[i].QueueFree();
		}
	}

	private void AddLobbyItem(Godot.Collections.Dictionary lobbyData)
	{
		// Utwórz kontener dla lobby item
		var lobbyItemContainer = new HBoxContainer();
		lobbyItemContainer.SetAnchorsPreset(Control.LayoutPreset.TopWide);
		
		// Informacje o lobby
		int index = (int)lobbyData["index"];
		string lobbyId = (string)lobbyData["lobbyId"];
		int currentPlayers = (int)lobbyData["currentPlayers"];
		int maxPlayers = (int)lobbyData["maxPlayers"];

		// Label z informacjami
		var lobbyInfoLabel = new Label();
		lobbyInfoLabel.Text = $"Lobby #{index + 1} - Players: {currentPlayers}/{maxPlayers}";
		lobbyInfoLabel.CustomMinimumSize = new Vector2(300, 0);
		lobbyItemContainer.AddChild(lobbyInfoLabel);

		// Przycisk Join
		var lobbyJoinButton = new Button();
		lobbyJoinButton.Text = "Join";
		lobbyJoinButton.CustomMinimumSize = new Vector2(100, 40);
		
		// Podłącz akcję join
		lobbyJoinButton.Pressed += () => OnJoinButtonPressed(index, lobbyId);
		
		lobbyItemContainer.AddChild(lobbyJoinButton);

		// Dodaj separator
		var lobbySeparator = new HSeparator();
		
		// Dodaj do listy
		AddChild(lobbyItemContainer);
		AddChild(lobbySeparator);
	}

	private void OnJoinButtonPressed(int index, string lobbyId)
	{
		GD.Print($"Joining lobby at index {index}: {lobbyId}");
		eosManager.JoinLobbyByIndex(index);
	}

	public override void _ExitTree()
	{
		// Odłącz sygnał
		if (eosManager != null)
		{
			eosManager.LobbyListUpdated -= OnLobbyListUpdated;
		}
	}
}
