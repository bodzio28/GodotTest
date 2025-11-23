extends Control

# --- 1. KONFIGURACJA ŚCIEŻEK ---
const SAVE_PATH = "user://settings.cfg"
# Upewnij się, że ta ścieżka do menu jest poprawna!
const MAIN_MENU_SCENE_PATH = "res://Scenes/MainMenu/main.tscn"

# --- 2. PRZYPISANIE ELEMENTÓW INTERFEJSU (NODÓW) ---
# WAŻNE: Sprawdź, czy nazwy po prawej zgadzają się z Twoim drzewem w edytorze!
@onready var master_slider = $SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/MasterSlider
@onready var music_slider = $SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/MusicSlider
@onready var sfx_slider = $SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/SFXSlider
@onready var mute_box = $SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/MuteCheckBox

@onready var save_button = $SettingsPanel/SettingsCenter/VSettings/SaveButton
@onready var return_button = $SettingsPanel/SettingsCenter/VSettings/ReturnButton

# --- 3. DANE (Domyślne ustawienia) ---
var config_data = {
	"sound": {
		"master_volume": 1.0,
		"music_volume": 1.0,
		"sfx_volume": 1.0,
		"is_muted": false
	},
	"video": {
		"display_mode": 0,
		"vsync": true
	}
}

# --- 4. START SCENY ---
func _ready():
	# Najpierw pobieramy ustawienia z pliku
	load_settings()
	
	# Aktualizujemy wygląd suwaków, żeby pasowały do wczytanych danych
	update_ui_elements()
	
	# Podłączamy sygnały (reakcje na kliknięcia)
	# Jeśli podłączyłeś je już w edytorze (zielona ikonka obok funkcji), to te linie można usunąć,
	# ale bezpieczniej jest zostawić je w kodzie.
	master_slider.value_changed.connect(_on_master_slider_changed)
	music_slider.value_changed.connect(_on_music_slider_changed)
	sfx_slider.value_changed.connect(_on_sfx_slider_changed)
	
	if mute_box: # Sprawdzamy czy mute_box istnieje, żeby nie wywaliło błędu
		mute_box.toggled.connect(_on_mute_toggled)
	
	save_button.pressed.connect(save_settings)
	return_button.pressed.connect(_on_back_button_pressed)

# --- 5. LOGIKA DANYCH (Zapis/Odczyt) ---
func load_settings():
	var config = ConfigFile.new()
	var err = config.load(SAVE_PATH)

	if err != OK:
		print("Brak pliku ustawień. Używam domyślnych.")
		apply_audio_settings() # Aplikujemy domyślne
		return

	# Pobieramy dane (jeśli czegoś brakuje, wstawiamy wartość domyślną)
	config_data.sound.master_volume = config.get_value("sound", "master_volume", 1.0)
	config_data.sound.music_volume = config.get_value("sound", "music_volume", 1.0)
	config_data.sound.sfx_volume = config.get_value("sound", "sfx_volume", 1.0)
	config_data.sound.is_muted = config.get_value("sound", "is_muted", false)
	
	config_data.video.display_mode = config.get_value("video", "display_mode", 0)
	config_data.video.vsync = config.get_value("video", "vsync", true)

	# Od razu aplikujemy to, co wczytaliśmy (żeby było słychać)
	apply_audio_settings()
	apply_video_settings()

func save_settings():
	var config = ConfigFile.new()

	# Wrzucamy aktualne dane do obiektu config
	config.set_value("sound", "master_volume", config_data.sound.master_volume)
	config.set_value("sound", "music_volume", config_data.sound.music_volume)
	config.set_value("sound", "sfx_volume", config_data.sound.sfx_volume)
	config.set_value("sound", "is_muted", config_data.sound.is_muted)
	
	config.set_value("video", "display_mode", config_data.video.display_mode)
	config.set_value("video", "vsync", config_data.video.vsync)

	# Zapisujemy fizycznie na dysk
	config.save(SAVE_PATH)
	print("Ustawienia zostały zapisane w: ", SAVE_PATH)

# --- 6. AKTUALIZACJA UI ---
func update_ui_elements():
	# Ustawiamy suwaki w dobrych miejscach
	master_slider.value = config_data.sound.master_volume
	music_slider.value = config_data.sound.music_volume
	sfx_slider.value = config_data.sound.sfx_volume
	
	if mute_box:
		mute_box.button_pressed = config_data.sound.is_muted

# --- 7. REAKCJA NA SUWAKI (Live Update) ---
func _on_master_slider_changed(value: float):
	config_data.sound.master_volume = value
	_set_bus_volume("Master", value)

func _on_music_slider_changed(value: float):
	config_data.sound.music_volume = value
	_set_bus_volume("Music", value)

func _on_sfx_slider_changed(value: float):
	config_data.sound.sfx_volume = value
	_set_bus_volume("SFX", value)

func _on_mute_toggled(is_muted: bool):
	config_data.sound.is_muted = is_muted
	var master_idx = AudioServer.get_bus_index("Master")
	AudioServer.set_bus_mute(master_idx, is_muted)

# --- 8. FUNKCJE POMOCNICZE (SYSTEMOWE) ---
func apply_audio_settings():
	_set_bus_volume("Master", config_data.sound.master_volume)
	_set_bus_volume("Music", config_data.sound.music_volume)
	_set_bus_volume("SFX", config_data.sound.sfx_volume)
	
	var master_idx = AudioServer.get_bus_index("Master")
	AudioServer.set_bus_mute(master_idx, config_data.sound.is_muted)

func _set_bus_volume(bus_name: String, linear_val: float):
	var bus_idx = AudioServer.get_bus_index(bus_name)
	# Jeśli linear_val == 0, dajemy -80dB (cisza absolutna), w przeciwnym razie konwertujemy
	var db_val = linear_to_db(linear_val) if linear_val > 0 else -80.0
	AudioServer.set_bus_volume_db(bus_idx, db_val)

func apply_video_settings():
	# V-Sync
	if config_data.video.vsync:
		DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_ENABLED)
	else:
		DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
		
	# Tryb okna
	match config_data.video.display_mode:
		0: DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED)
		1: DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN)
		2: DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_FULLSCREEN)

# --- 9. NAWIGACJA ---
func _on_back_button_pressed():
	# Sprawdź czy plik sceny MainMenu faktycznie istnieje pod tą ścieżką!
	if ResourceLoader.exists(MAIN_MENU_SCENE_PATH):
		get_tree().change_scene_to_file(MAIN_MENU_SCENE_PATH)
	else:
		print("BŁĄD: Nie znaleziono sceny menu pod ścieżką: ", MAIN_MENU_SCENE_PATH)
