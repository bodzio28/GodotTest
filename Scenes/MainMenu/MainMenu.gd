extends Control

# Preload scenes
const LOBBY_CREATE_SCENE = preload("res://Scenes/LobbyCreate/LobbyCreate.tscn")
const LOBBY_SEARCH_SCENE = preload("res://Scenes/LobbySearch/LobbySearch.tscn")
const HELP_SCENE = preload("res://Scenes/HelpScene/HelpScene.tscn")
const SETTINGS_SCENE = preload("res://Scenes/SettingsScene/SettingsScene.tscn")

@onready var quit_confirmation_dialog = $QuitGameConfarmation

func _ready() -> void:
	# Connect button signals
	var create_button = $Panel/MenuCenter/VMenu/CreateGame/CreateGameButton
	var join_button = $Panel/MenuCenter/VMenu/JoinGame/JoinGameButton
	var quit_button = $Panel/MenuCenter/VMenu/Quit/QuitButton
	var help_button = $Panel/MenuCenter/VMenu/Help/HelpButton
	var settings_button = $Panel/MenuCenter/VMenu/Settings/SettingsButton
	
	
	create_button.pressed.connect(_on_create_game_pressed)
	join_button.pressed.connect(_on_join_game_pressed)
	quit_button.pressed.connect(_on_quit_pressed)
	help_button.pressed.connect(_on_help_pressed)
	settings_button.pressed.connect(_on_settings_pressed)
	quit_confirmation_dialog.confirmed.connect(_on_quit_confirmation_dialog_confirmed)
	
func _on_create_game_pressed() -> void:
	print("Loading Lobby Create scene...")
	get_tree().change_scene_to_packed(LOBBY_CREATE_SCENE)

func _on_join_game_pressed() -> void:
	print("Loading Lobby Search scene...")
	get_tree().change_scene_to_packed(LOBBY_SEARCH_SCENE)

func _on_quit_pressed() -> void:
	print("Showing quit confirmation dialog...")
	quit_confirmation_dialog.popup_centered()
	
func _on_quit_confirmation_dialog_confirmed() -> void:
	print("Quit confirmed, exiting game.")
	get_tree().quit()
	
func _on_help_pressed() -> void:
	print("Loading Help Scene...")
	get_tree().change_scene_to_packed(HELP_SCENE)
func _on_settings_pressed()-> void:
	print("Loading Settings Scene...")
	get_tree().change_scene_to_packed(SETTINGS_SCENE)
