using Godot;
using System;


namespace EOSStructs
{
    public partial class CreateOptions : RefCounted
	{
		Variant product_id = "e0fad88fbfc147ddabce0900095c4f7b";
		Variant sandbox_id = "ce451c8e18ef4cb3bc7c5cdc11a9aaae";
		Variant client_id = "xyza7891eEYHFtDWNZaFlmauAplnUo5H";
		Variant client_secret = "xD8rxykYUyqoaGoYZ5zhK+FD6Kg8+LvkATNkDb/7DPo";	
		Variant deployment_id = "0e28b5f3257a4dbca04ea0ca1c30f265";

	}


	public partial class InitializeOptions : RefCounted
	{
		Variant product_name = "LobbySample";
		Variant product_version = "1.0.0";
	}
}

