using Godot;
using System;

public partial class UiManager : Node
{
	private EOSManager EOSManager;
	private CurrentLobbyPanel currentLobbyPanel;
	private Button createLobbyButton;
	
	public override void _Ready()
	{
		EOSManager = ((EOSManager)GetNode("/root/EOSManager"));
		
		// Pobierz referencję do przycisku Create Lobby (scena LobbyCreate)
		var parent = GetParent();
		if (parent is Control control)
		{
			createLobbyButton = control.GetNodeOrNull<Button>("CreateLobby");
		}
		
		// NOWE: Zawsze twórz CurrentLobbyPanel (dla joiners też!) UwU
		CreateCurrentLobbyPanel();
		
		// Podłącz sygnały z EOSManager
		EOSManager.LobbyCreated += OnLobbyCreated;
		EOSManager.LobbyJoined += OnLobbyJoined;
		EOSManager.LobbyCreationFailed += OnLobbyCreationFailed;
	}
	
	private void CreateCurrentLobbyPanel()
	{
		// Stwórz panel
		currentLobbyPanel = new CurrentLobbyPanel();
		currentLobbyPanel.Position = new Vector2(41, 167);
		currentLobbyPanel.Size = new Vector2(405, 258);
		
		// Dodaj do Control node (parent)
		var parent = GetParent();
		if (parent is Control control)
		{
			control.CallDeferred("add_child", currentLobbyPanel);
			GD.Print("✅ CurrentLobbyPanel created programmatically");
		}
	}

	public void OnCreateLobbyButtonPressed()
	{
		// Zablokuj przycisk podczas tworzenia
		if (createLobbyButton != null)
		{
			createLobbyButton.Disabled = true;
			createLobbyButton.Text = "Creating...";
		}
		
		EOSManager.CreateLobby("Moje Lobby", 4, true);
	}

	public void OnJoinLobbyButtonPressed()
	{
		// Wyszukaj lobby - lista zostanie zaktualizowana przez sygnał LobbyListUpdated
		EOSManager.SearchLobbies();
	}

	// Nowa funkcja do dołączania po indeksie
	public void JoinFirstLobby()
	{
		EOSManager.JoinLobbyByIndex(0); // Dołącz do pierwszego lobby z listy
	}

	// Callback gdy lobby zostało utworzone
	private void OnLobbyCreated(string lobbyId)
	{
		GD.Print($"[UI] Lobby created: {lobbyId}");
		
		// Odblokuj przycisk i przywróć tekst
		if (createLobbyButton != null)
		{
			createLobbyButton.Disabled = false;
			createLobbyButton.Text = "create lobby";
		}
	}
	
	// Callback gdy tworzenie lobby się nie powiodło
	private void OnLobbyCreationFailed(string errorMessage)
	{
		GD.PrintRich($"[color=yellow][UI] Lobby creation failed: {errorMessage}");
		
		// Odblokuj przycisk i przywróć tekst
		if (createLobbyButton != null)
		{
			createLobbyButton.Disabled = false;
			createLobbyButton.Text = "create lobby";
		}
	}

	// Callback gdy dołączono do lobby
	private void OnLobbyJoined(string lobbyId)
	{
		GD.Print($"[UI] Joined lobby: {lobbyId}");
		// Możesz tu np. przejść do lobby scene
	}

	public override void _ExitTree()
	{
		if (EOSManager != null)
		{
			EOSManager.LobbyCreated -= OnLobbyCreated;
			EOSManager.LobbyJoined -= OnLobbyJoined;
			EOSManager.LobbyCreationFailed -= OnLobbyCreationFailed;
		}
	}
}

