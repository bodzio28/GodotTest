using Godot;
using System;

namespace Networking;

// NOTE: Class name must match filename exactly (case-sensitive) for Godot C# scripts.
public partial class productDetails : Node
{
    [Export]
    public string product_name = "Networking";
    [Export]
    public string product_version = "1.0";
    [Export]
    public string product_id = "e0fad88fbfc147ddabce0900095c4f7b";
    [Export]
    public string sandbox_id = "ce451c8e18ef4cb3bc7c5cdc11a9aaae";
    [Export]
    public string deployment_id = "0e28b5f3257a4dbca04ea0ca1c30f265";
    [Export]
    public string client_id = "xyza7891Njqz2Q69q8rIimAO8cat2qAY";
    [Export]
    public string client_secret = "acBStNjuOyjPF9ISHw7ffa4VvO25lWEyxi5Mez8+PGQ";
    [Export]
    public string encryption_key = "";

    public override void _Ready()
    {
        // ...wartości są dostępne tutaj...
    }
}