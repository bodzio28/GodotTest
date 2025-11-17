extends Control

const MAIN_MENU_SCENE = preload("res://Scenes/MainMenu/main.tscn")
func _ready():
	$HelpPanel/HelpCenter/VHelp/ReturnButton.pressed.connect(_on_back_button_pressed)

func _on_back_button_pressed():
	print("Powrót do menu głównego...")
	get_tree().change_scene_to_file("res://Scenes/MainMenu/main.tscn")
