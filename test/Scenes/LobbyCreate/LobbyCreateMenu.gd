extends Control

var eos_manager: EOSManager

func _ready() -> void:
	eos_manager = get_node("/root/EOSManager")
	
	# Podłącz przycisk ustawiania nicku
	var set_nick_button = $NicknamePanel/SetNicknameButton
	set_nick_button.pressed.connect(_on_set_nickname_pressed)

func _on_set_nickname_pressed() -> void:
	var nickname_edit = $NicknamePanel/NicknameEdit
	var nickname = nickname_edit.text.strip_edges()
	if nickname != "":
		eos_manager.SetPendingNickname(nickname)
		print("✅ Nickname set: ", nickname)
	else:
		print("⚠️ Nickname is empty")

func _on_back_button_pressed() -> void:
	print("Returning to main menu...")
	# Opuść lobby jeśli jesteś w jakimś
	if eos_manager != null and eos_manager.currentLobbyId != "":
		print("🚪 Leaving lobby before returning to menu...")
		eos_manager.LeaveLobby()
	get_tree().change_scene_to_file("res://Scenes/MainMenu/main.tscn")
