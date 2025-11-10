extends Control

# Preload scenes
const LOBBY_CREATE_SCENE = preload("res://Scenes/LobbyCreate/LobbyCreate.tscn")
const LOBBY_SEARCH_SCENE = preload("res://Scenes/LobbySearch/LobbySearch.tscn")

func _ready() -> void:
	# Connect button signals
	var create_button = $Panel/MenuCenter/VMenu/CreateGame/CreateGameButton
	var join_button = $Panel/MenuCenter/VMenu/JoinGame/JoinGameButton
	var quit_button = $Panel/MenuCenter/VMenu/Quit/QuitButton
	
	create_button.pressed.connect(_on_create_game_pressed)
	join_button.pressed.connect(_on_join_game_pressed)
	quit_button.pressed.connect(_on_quit_pressed)

func _on_create_game_pressed() -> void:
	print("Loading Lobby Create scene...")
	get_tree().change_scene_to_packed(LOBBY_CREATE_SCENE)

func _on_join_game_pressed() -> void:
	print("Loading Lobby Search scene...")
	get_tree().change_scene_to_packed(LOBBY_SEARCH_SCENE)

func _on_quit_pressed() -> void:
	print("Quitting game...")
	get_tree().quit()
