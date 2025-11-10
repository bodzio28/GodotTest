extends Control

var eos_manager: EOSManager

func _ready() -> void:
	eos_manager = get_node("/root/EOSManager")

func _on_back_button_pressed() -> void:
	print("Returning to main menu...")
	# Opuść lobby jeśli jesteś w jakimś
	if eos_manager != null and eos_manager.currentLobbyId != "":
		print("🚪 Leaving lobby before returning to menu...")
		eos_manager.LeaveLobby()
	get_tree().change_scene_to_file("res://Scenes/MainMenu/main.tscn")


