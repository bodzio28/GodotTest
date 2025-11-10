extends Node

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

	EOS.Logging.set_log_level(EOS.Logging.LogCategory.AllCategories, EOS.Logging.LogLevel.VeryVerbose)
	IEOS.logging_interface_callback.connect(logEOS)
	
	#IEOS.platform_interface_create(create_opts);
	EOS.Platform.PlatformInterface.initialize(init_opts);
	EOS.Platform.PlatformInterface.create(create_opts);
	
#pass


func logEOS(logMessage : Dictionary) -> void:
	#print_rich("[color=red]" + logMessage.category);
	print_rich("[color=green]" + logMessage.message);
#pass
