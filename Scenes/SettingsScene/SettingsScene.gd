extends Node

# Ta ścieżka będzie działać na PC i automatycznie w przeglądarce!
const SAVE_PATH = "user://settings.cfg"
const MAIN_MENU_SCENE = preload("res://Scenes/MainMenu/main.tscn")

# Domyślne wartości
var config_data = {
	"sound": {
		"master_volume": 1.0,
		"music_volume": 1.0,
		"sfx_volume": 1.0,
		"is_muted": false
	},
	"video": {
		"display_mode": 0, # 0 = W Oknie, 1 = Pełny Ekran, 2 = Bez Ramki
		"vsync": true
	}
}

func _ready():
	load_settings()
	$SettingsPanel/SettingsCenter/VSettings/ReturnButton.pressed.connect(_on_back_button_pressed)

# Wczytuje ustawienia z pliku (lub z Local Storage w przeglądarce)
func load_settings():
	var config = ConfigFile.new()
	var err = config.load(SAVE_PATH)

	# Jeśli nie ma pliku/zapisu, stwórz nowy z domyślnymi
	if err != OK:
		print("Nie znaleziono pliku ustawień, tworzę nowy.")
		save_settings() 
		return

	# Wczytaj zapisane wartości
	config_data.sound.master_volume = config.get_value("sound", "master_volume", 1.0)
	config_data.sound.music_volume = config.get_value("sound", "music_volume", 1.0)
	config_data.sound.sfx_volume = config.get_value("sound", "sfx_volume", 1.0)
	config_data.sound.is_muted = config.get_value("sound", "is_muted", false)
	
	config_data.video.display_mode = config.get_value("video", "display_mode", 0)
	config_data.video.vsync = config.get_value("video", "vsync", true)

	# Zastosuj ustawienia natychmiast po wczytaniu
	apply_all_settings()

# Zapisuje ustawienia do pliku (lub do Local Storage w przeglądarce)
func save_settings():
	var config = ConfigFile.new()

	config.set_value("sound", "master_volume", config_data.sound.master_volume)
	config.set_value("sound", "music_volume", config_data.sound.music_volume)
	config.set_value("sound", "sfx_volume", config_data.sound.sfx_volume)
	config.set_value("sound", "is_muted", config_data.sound.is_muted)
	
	config.set_value("video", "display_mode", config_data.video.display_mode)
	config.set_value("video", "vsync", config_data.video.vsync)

	config.save(SAVE_PATH)

func apply_all_settings():
	set_volume("Master", config_data.sound.master_volume)
	set_volume("Music", config_data.sound.music_volume)
	set_volume("SFX", config_data.sound.sfx_volume)
	set_mute(config_data.sound.is_muted)
	
	set_display_mode(config_data.video.display_mode)
	set_vsync(config_data.video.vsync)


# --- Funkcje do zmiany i ZAPISYWANIA ---

func set_volume(bus_name, linear_value):
	if bus_name == "Master":
		config_data.sound.master_volume = linear_value
	elif bus_name == "Music":
		config_data.sound.music_volume = linear_value
	elif bus_name == "SFX":
		config_data.sound.sfx_volume = linear_value

	var bus_index = AudioServer.get_bus_index(bus_name)
	AudioServer.set_bus_volume_db(bus_index, linear_to_db(linear_value))
	save_settings()

func set_mute(is_muted):
	config_data.sound.is_muted = is_muted
	var bus_index = AudioServer.get_bus_index("Master")
	AudioServer.set_bus_mute(bus_index, is_muted)
	save_settings()

func set_display_mode(mode_index):
	config_data.video.display_mode = mode_index
	match mode_index:
		0: 
			DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED)
		1: 
			DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN)
		2: 
			DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_FULLSCREEN)
	save_settings() 

func set_vsync(is_on):
	config_data.video.vsync = is_on
	if is_on:
		DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_ENABLED)
	else:
		DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	save_settings() 


#Funkcja do powrotu
func _on_back_button_pressed():
	print("Powrót do menu głównego...")
	get_tree().change_scene_to_file("res://Scenes/MainMenu/main.tscn")
