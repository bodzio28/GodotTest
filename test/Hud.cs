using Godot;

public partial class Hud : Control
{
    private Button _hostBtn;
    private Button _joinBtn;
    private LineEdit _addressEdit;
    private LineEdit _portEdit;

    public override void _Ready()
    {
        _hostBtn = GetNode<Button>("VBox/Buttons/HostButton");
        _joinBtn = GetNode<Button>("VBox/Buttons/JoinButton");
        _addressEdit = GetNode<LineEdit>("VBox/Inputs/Address");
        _portEdit = GetNode<LineEdit>("VBox/Inputs/Port");

        _hostBtn.Pressed += OnHost;
        _joinBtn.Pressed += OnJoin;
    }

    private NetworkManager GetNet()
    {
        return GetTree().Root.GetNode<NetworkManager>("Main/NetworkManager");
    }

    private void OnHost()
    {
        if (int.TryParse(_portEdit.Text, out var port))
        {
            GetNet().Port = port;
        }
        GetNet().Host();
    }

    private void OnJoin()
    {
        var net = GetNet();
        if (!string.IsNullOrWhiteSpace(_addressEdit.Text))
            net.Address = _addressEdit.Text.Trim();
        if (int.TryParse(_portEdit.Text, out var port))
            net.Port = port;
        net.Join();
    }
}
