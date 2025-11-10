# Test Lobby Searcha na Lobby zrobionym przez SDK C#
# Wymaga Platformy i zalogowanego użytkownika na Addonie
# ~~konsultant Godotowcowy

extends Node


# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	var init_opts = EOS.Platform.InitializeOptions.new()
	init_opts.product_name ="WZIMniacy"
	init_opts.product_version ="1.0"

	
	var create_opts = EOS.Platform.CreateOptions.new()
	
	create_opts.product_id ="e0fad88fbfc147ddabce0900095c4f7b"
	create_opts.sandbox_id="ce451c8e18ef4cb3bc7c5cdc11a9aaae"
	create_opts.client_id="xyza7891eEYHFtDWNZaFlmauAplnUo5H"
	create_opts.client_secret="xD8rxykYUyqoaGoYZ5zhK+FD6Kg8+LvkATNkDb/7DPo" 
	create_opts.deployment_id="0e28b5f3257a4dbca04ea0ca1c30f265"

	EOS.Logging.set_log_level(
		EOS.Logging.LogCategory.AllCategories,
		EOS.Logging.LogLevel.VeryVerbose
	)
	IEOS.logging_interface_callback.connect(log_eos)
	
	#IEOS.platform_interface_create(create_opts);
	EOS.Platform.PlatformInterface.initialize(init_opts);
	EOS.Platform.PlatformInterface.create(create_opts);
	
	var device_id_options = EOS.Connect.CreateDeviceIdOptions.new()
	device_id_options.device_model = "NIGGER PHONE"
	EOS.Connect.ConnectInterface.create_device_id(device_id_options)
	
	var login_options = EOS.Connect.LoginOptions.new()
	var credentials = EOS.Connect.Credentials.new()
	var user_login_info = EOS.Connect.UserLoginInfo.new()
	
	credentials.type = EOS.ExternalCredentialType.DeviceidAccessToken
	credentials.token = null
	
	user_login_info.display_name = "player_nigerion"
	
	login_options.credentials = credentials
	login_options.user_login_info = user_login_info
	
	EOS.Connect.ConnectInterface.login(login_options)
	
	
	
	
#pass


func log_eos(log_message: Dictionary) -> void:
	#print_rich("[color=red]" + log_message.category);
	print_rich("[color=green]" + log_message.message)
#pass



# Called every frame. 'delta' is the elapsed time since the previous frame.
#func _process(delta: float) -> void:
	#IEOS.tick()
	#pass


func _on_create_lobby_3_pressed() -> void:
	var search_options = EOS.Lobby.CreateLobbySearchOptions.new()
	#search_options.max_results = 25
	
	var lobby_search_result = EOS.Lobby.LobbyInterface.create_lobby_search(
		search_options
	)
	var lobby_search: EOSGLobbySearch = lobby_search_result.lobby_search
	
	#var lobby_search_options = EOS.Lobby.CreateLobbySearchOptions.new()
	#var local_product_user_id = get_node("/root/EOSManager").localProductUserIdString
	var local_product_user_id = EOSGRuntime.local_product_user_id
	var current_lobby_id = get_node("/root/EOSManager").currentLobbyId
	print(current_lobby_id + "NIGGER1")
	lobby_search.set_parameter("bucket", "DefaultBucket", EOS.ComparisonOp.Equal)
	
	#lobby_search.set_lobby_id(current_lobby_id)
	#var set_parameter_options = EOS.Lobby.lobby

	#lobby_search.set_target_user_id(abc)
	#print(local_product_user_id)
	var count: int = lobby_search.get_search_result_count()
	print(str(count) + "NIGGERSprzedprzed")
	
	lobby_search.find(local_product_user_id)
	
	count = lobby_search.get_search_result_count()
	print(str(count) + "NIGGERSprzed")
	
	#var search_ret = await IEOS.lobby_search_find_callback
	await IEOS.lobby_search_find_callback
	
	count = lobby_search.get_search_result_count()
	#print(lobby_search_result.lobby_search)
	print(str(count) + "NIGGERSpo")
	# Replace with function body.
