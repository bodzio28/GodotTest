extends Button
# --- ŁADOWANIE PLIKÓW ---
# Upewnij się, że ścieżki są poprawne!
const AUDIO_HOVER = preload("res://Sounds/Hover.ogg")
const AUDIO_BUTTON = preload("res://Sounds/Button.ogg")
const AUDIO_BG_MUSIC = preload("res://Sounds/BackGround.mp3")

# ZMIENNE NA ODTWARZACZE
var music_player: AudioStreamPlayer
var sfx_hover: AudioStreamPlayer
var sfx_click: AudioStreamPlayer

func _ready() -> void:
	# 1. Konfiguracja odtwarzaczy przy starcie gry
	_setup_audio_players()
	
	# 2. Start muzyki
	play_music()
	
	# 3. Podłączamy się do sygnału drzewa scen.
	get_tree().node_added.connect(_on_node_added)

# KONFIGURACJA
func _setup_audio_players() -> void:
	# Muzyka
	music_player = AudioStreamPlayer.new()
	music_player.stream = AUDIO_BG_MUSIC
	music_player.volume_db = -15.0
	music_player.process_mode = Node.PROCESS_MODE_ALWAYS
	music_player.bus = "Music"
	add_child(music_player)
	
	#CLICK
	sfx_click = AudioStreamPlayer.new()
	sfx_click.stream = AUDIO_BUTTON
	sfx_click.volume_db = -5.0
	sfx_click.bus = "SFX"
	add_child(sfx_click)
	
	# HOVER
	sfx_hover = AudioStreamPlayer.new()
	sfx_hover.stream = AUDIO_HOVER
	sfx_hover.volume_db = -10.0
	sfx_hover.bus = "SFX"
	add_child(sfx_hover)

# START MUZYKI
func play_music() -> void:
	if not music_player.playing:
		music_player.play()

# AUTOMATYCZNE WYKRYWANIE PRZYCISKÓW
func _on_node_added(node: Node) -> void:
	# Jeśli dodany element to Przycisk (Button) LUB TextureButton...
	if node is Button or node is TextureButton:
		# ...i nie podłączyliśmy go jeszcze do naszego systemu dźwiękowego:
		if not node.mouse_entered.is_connected(_play_hover):
			# Podłączamy dźwięki
			node.mouse_entered.connect(_play_hover)
			node.pressed.connect(_play_click)

# ODTWARZANIE EFEKTÓW
func _play_hover() -> void:
	# Opcjonalny Randomizer, żeby nie brzmiało jak robot
	sfx_hover.pitch_scale = randf_range(0.95, 1.05)
	sfx_hover.play()

func _play_click() -> void:
	sfx_click.play()
