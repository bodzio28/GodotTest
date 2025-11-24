extends Control

const SAVE_PATH = "user://settings.cfg"
# Upewnij się, że ta ścieżka jest poprawna
const MAIN_MENU_SCENE_PATH = "res://Scenes/MainMenu/main.tscn" 

# --- DŹWIĘK ---
@onready var master_slider = $SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/MasterSlider
@onready var music_slider = $SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/MusicSlider
@onready var sfx_slider = $SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/SFXSlider
@onready var mute_box = $SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/MuteCheckBox

# --- WIDEO ---
@onready var screen_option = $SettingsPanel/SettingsCenter/VSettings/TabContainer/Video/ScreenOption
@onready var resolution_option = $SettingsPanel/SettingsCenter/VSettings/TabContainer/Video/ResolutionOption
@onready var scale_slider = $SettingsPanel/SettingsCenter/VSettings/TabContainer/Video/SliderUI

# --- PRZYCISKI ---
@onready var save_button = $SettingsPanel/SettingsCenter/VSettings/SaveButton
@onready var return_button = $SettingsPanel/SettingsCenter/VSettings/ReturnButton

# Lista dostępnych rozdzielczości
var resolutions: Array[Vector2i] = [
	Vector2i(1920, 1080),
	Vector2i(1600, 900),
	Vector2i(1366, 768),
	Vector2i(1280, 720)
]

# Domyślne ustawienia (rozszerzone o wideo)
var config_data = {
	"sound": {
		"master_volume": 1.0,
		"music_volume": 1.0,
		"sfx_volume": 1.0,
		"is_muted": false
	},
	"video": {
		"display_mode": 0, # 0 = Windowed, 1 = Fullscreen
		"resolution_index": 0, # Domyślnie 1920x1080 (index 0)
		"ui_scale": 1.0,
		"vsync": true
	}
}

# START SCENY
func _ready():
	# 1. Najpierw konfigurujemy puste listy (UI), żeby były gotowe na przyjęcie danych
	setup_video_ui_options()
	
	# 2. Wczytujemy ustawienia z pliku
	load_settings()
	
	# 3. Aktualizujemy wygląd suwaków/list na podstawie wczytanych danych
	update_ui_elements()
	
	# 4. Podłączamy sygnały (Audio)
	master_slider.value_changed.connect(_on_master_slider_changed)
	music_slider.value_changed.connect(_on_music_slider_changed)
	sfx_slider.value_changed.connect(_on_sfx_slider_changed)
	if mute_box:
		mute_box.toggled.connect(_on_mute_toggled)
	
	# 5. Podłączamy sygnały (Wideo) - NOWE
	screen_option.item_selected.connect(_on_window_mode_selected)
	resolution_option.item_selected.connect(_on_resolution_selected)
	scale_slider.value_changed.connect(_on_scale_slider_changed)
	
	# 6. Podłączamy przyciski
	save_button.pressed.connect(save_settings)
	return_button.pressed.connect(_on_back_button_pressed)

# --- KONFIGURACJA UI (Initial Setup) ---
func setup_video_ui_options():
	# Konfiguracja listy Trybu Okna
	screen_option.clear()
	screen_option.add_item("W Oknie", 0)
	screen_option.add_item("Pełny Ekran", 1)
	
	# Konfiguracja listy Rozdzielczości
	resolution_option.clear()
	for i in range(resolutions.size()):
		var res = resolutions[i]
		var label = str(res.x) + " x " + str(res.y)
		resolution_option.add_item(label, i)

	# Konfiguracja Slidera Skali
	scale_slider.min_value = 0.5
	scale_slider.max_value = 1.5
	scale_slider.step = 0.1

# --- LOGIKA DANYCH (Zapis/Odczyt) ---
func load_settings():
	var config = ConfigFile.new()
	var err = config.load(SAVE_PATH)

	if err != OK:
		print("Brak pliku ustawień. Używam domyślnych.")
		apply_audio_settings()
		apply_video_settings() # Aplikujemy domyślne
		return

	# DŹWIĘK
	config_data.sound.master_volume = config.get_value("sound", "master_volume", 1.0)
	config_data.sound.music_volume = config.get_value("sound", "music_volume", 1.0)
	config_data.sound.sfx_volume = config.get_value("sound", "sfx_volume", 1.0)
	config_data.sound.is_muted = config.get_value("sound", "is_muted", false)
	
	# WIDEO
	config_data.video.display_mode = config.get_value("video", "display_mode", 0)
	config_data.video.resolution_index = config.get_value("video", "resolution_index", 0)
	config_data.video.ui_scale = config.get_value("video", "ui_scale", 1.0)
	config_data.video.vsync = config.get_value("video", "vsync", true)

	# Aplikujemy ustawienia do gry
	apply_audio_settings()
	apply_video_settings()

func save_settings():
	var config = ConfigFile.new()

	# Dźwięk
	config.set_value("sound", "master_volume", config_data.sound.master_volume)
	config.set_value("sound", "music_volume", config_data.sound.music_volume)
	config.set_value("sound", "sfx_volume", config_data.sound.sfx_volume)
	config.set_value("sound", "is_muted", config_data.sound.is_muted)
	
	# Wideo
	config.set_value("video", "display_mode", config_data.video.display_mode)
	config.set_value("video", "resolution_index", config_data.video.resolution_index)
	config.set_value("video", "ui_scale", config_data.video.ui_scale)
	config.set_value("video", "vsync", config_data.video.vsync)

	config.save(SAVE_PATH)
	print("Ustawienia zostały zapisane w: ", SAVE_PATH)

# --- AKTUALIZACJA ELEMENTÓW UI (Wizualne ustawienie suwaków) ---
func update_ui_elements():
	# Dźwięk
	master_slider.value = config_data.sound.master_volume
	music_slider.value = config_data.sound.music_volume
	sfx_slider.value = config_data.sound.sfx_volume
	if mute_box: mute_box.button_pressed = config_data.sound.is_muted
	
	# Wideo
	screen_option.select(config_data.video.display_mode)
	resolution_option.select(config_data.video.resolution_index)
	scale_slider.value = config_data.video.ui_scale
	
	# Blokowanie rozdzielczości jeśli jest fullscreen
	_check_resolution_lock()

# --- OBSŁUGA SYGNAŁÓW (Zmiany przez gracza) ---

# AUDIO
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

# WIDEO (Nowe)
func _on_window_mode_selected(index: int):
	config_data.video.display_mode = index
	
	match index:
		0: # --- TRYB: W OKNIE ---
			# 1. Najpierw ustawiamy tryb okienkowy
			DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED)
			
			# 2. WAŻNE: Upewniamy się, że okno ma ramki (inaczej wygląda jak fullscreen)
			DisplayServer.window_set_flag(DisplayServer.WINDOW_FLAG_BORDERLESS, false)
			
			# 3. Pobieramy aktualnie wybraną rozdzielczość z pamięci
			var res_idx = config_data.video.resolution_index
			# Zabezpieczenie: jeśli indeks jest poza zakresem, ustawiamy domyślnie 1920x1080 (indeks 0)
			if res_idx < 0 or res_idx >= resolutions.size():
				res_idx = 0
			
			var size = resolutions[res_idx]
			
			# 4. Wymuszamy ustawienie rozmiaru
			DisplayServer.window_set_size(size)
			
			# 5. Centrujemy (z małym opóźnieniem dla pewności, choć tu wywołujemy bezpośrednio)
			center_window()
			
			# Odblokowujemy wybór rozdzielczości
			resolution_option.disabled = false

		1: # --- TRYB: PEŁNY EKRAN ---
			# Ustawiamy Exclusive Fullscreen (najlepsza wydajność)
			DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN)
			
			# Blokujemy zmianę rozdzielczości, bo w Fullscreenie decyduje monitor
			resolution_option.disabled = true
func _on_resolution_selected(index: int):
	config_data.video.resolution_index = index
	var size = resolutions[index]
	
	# Zmieniamy rozmiar tylko w trybie okienkowym
	if DisplayServer.window_get_mode() == DisplayServer.WINDOW_MODE_WINDOWED:
		DisplayServer.window_set_size(size)
		center_window()

func _on_scale_slider_changed(value: float):
	config_data.video.ui_scale = value
	get_tree().root.content_scale_factor = value

# --- FUNKCJE APLIKUJĄCE (Systemowe) ---

func apply_audio_settings():
	_set_bus_volume("Master", config_data.sound.master_volume)
	_set_bus_volume("Music", config_data.sound.music_volume)
	_set_bus_volume("SFX", config_data.sound.sfx_volume)
	var master_idx = AudioServer.get_bus_index("Master")
	AudioServer.set_bus_mute(master_idx, config_data.sound.is_muted)

func _set_bus_volume(bus_name: String, linear_val: float):
	var bus_idx = AudioServer.get_bus_index(bus_name)
	var db_val = linear_to_db(linear_val) if linear_val > 0 else -80.0
	AudioServer.set_bus_volume_db(bus_idx, db_val)

func apply_video_settings():
	# VSync
	if config_data.video.vsync:
		DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_ENABLED)
	else:
		DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	
	# Tryb okna
	match config_data.video.display_mode:
		0: DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED)
		1: DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN)
	
	# Rozdzielczość i centrowanie (tylko dla okna)
	if config_data.video.display_mode == 0:
		var res_idx = config_data.video.resolution_index
		if res_idx >= 0 and res_idx < resolutions.size():
			DisplayServer.window_set_size(resolutions[res_idx])
			center_window()
			
	# Skala UI
	get_tree().root.content_scale_factor = config_data.video.ui_scale

# --- HELPERY ---

func center_window():
	var screen_id = DisplayServer.window_get_current_screen()
	var screen_size = DisplayServer.screen_get_size(screen_id)
	var window_size = DisplayServer.window_get_size()
	var origin = DisplayServer.screen_get_position(screen_id)
	var center_pos = origin + (screen_size / 2) - (window_size / 2)
	DisplayServer.window_set_position(center_pos)

func _check_resolution_lock():
	# Jeśli pełny ekran, blokujemy zmianę rozdzielczości (bo i tak jest natywna)
	if config_data.video.display_mode == 1:
		resolution_option.disabled = true
	else:
		resolution_option.disabled = false

func _on_back_button_pressed():
	if ResourceLoader.exists(MAIN_MENU_SCENE_PATH):
		get_tree().change_scene_to_file(MAIN_MENU_SCENE_PATH)
	else:
		print("BŁĄD: Nie znaleziono sceny menu pod ścieżką: ", MAIN_MENU_SCENE_PATH)
