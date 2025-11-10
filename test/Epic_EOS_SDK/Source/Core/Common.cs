// Copyright Epic Games, Inc. All Rights Reserved.

#if DEBUG
	#define EOS_DEBUG
#endif

#if TOOLS
	#define EOS_EDITOR
#endif

#if GODOT
	#define EOS_GODOT
#endif

#if GODOT_WINDOWS
	#if GODOT_64
		#define EOS_PLATFORM_WINDOWS_64
	#elif GODOT_32
		#define EOS_PLATFORM_WINDOWS_32
	#endif

#elif GODOT_MACOS || GODOT_OSX
	#define EOS_PLATFORM_OSX

#elif GODOT_LINUXBSD
	#define EOS_PLATFORM_LINUX

#elif GODOT_IOS
	#define EOS_PLATFORM_IOS

#elif GODOT_ANDROID
	#define EOS_PLATFORM_ANDROID

#endif

using System.Runtime.InteropServices;

namespace Epic.OnlineServices
{
	public sealed partial class Common
	{
		public const string LIBRARY_NAME =
		#if EOS_PLATFORM_WINDOWS_32 && EOS_GODOT
			"EOSSDK-Win32-Shipping"
		#elif EOS_PLATFORM_WINDOWS_32
			"EOSSDK-Win32-Shipping.dll"

		#elif EOS_PLATFORM_WINDOWS_64 && EOS_GODOT
			"EOSSDK-Win64-Shipping"
		#elif EOS_PLATFORM_WINDOWS_64
			"EOSSDK-Win64-Shipping.dll"

		#elif EOS_PLATFORM_OSX && EOS_GODOT
			"libEOSSDK-Mac-Shipping"
		#elif EOS_PLATFORM_OSX
			"libEOSSDK-Mac-Shipping.dylib"

		#elif EOS_PLATFORM_LINUX && EOS_GODOT
			"libEOSSDK-Linux-Shipping"
		#elif EOS_PLATFORM_LINUX
			"libEOSSDK-Linux-Shipping.so"

		#elif EOS_PLATFORM_IOS && EOS_GODOT && EOS_EDITOR
			"EOSSDK"
		#elif EOS_PLATFORM_IOS
			"EOSSDK.framework/EOSSDK"

		#elif EOS_PLATFORM_ANDROID
			"EOSSDK"

		#else
			#error Unable to determine name of the EOSSDK library. Ensure your project defines the correct EOS symbol for your platform, such as EOS_PLATFORM_WINDOWS_64, or define it here if it hasn't been already.
			"EOSSDK-UnknownPlatform-Shipping"

		#endif
		;
		
		public const CallingConvention LIBRARY_CALLING_CONVENTION =
		#if EOS_PLATFORM_WINDOWS_32
			CallingConvention.StdCall
		#else
			CallingConvention.Cdecl
		#endif
		;
	}
}