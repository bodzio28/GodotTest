using Godot;
using System;

public partial class EOSMenager : Node
{
	[Export]
	public Node test;


	public override void _Ready()
	{
		//IeosInstance instance = new IeosInstance();
		// GD.Print(Ieos.Singleton);
		// RefCounted abc = (RefCounted)Engine.GetSingleton("IEOS");
		Variant abc1 = test.Call("get_instance1");
		IeosInstance abc2 = (IeosInstance)abc1;
		// Variant abc = Engine.GetSingleton("IEOS").Call("get_instance");
		// IeosInstance instance = (IeosInstance)abc;

		// Ieos.Singleton.PlatformInterfaceInitialize(new EOSStructs.InitializeOptions());
		GD.Print(Engine.GetSingletonList());
		
		
	}
	


	
}
