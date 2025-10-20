using Godot;

public partial class Player : CharacterBody2D
{
    [Export] public float Speed { get; set; } = 220f;
    [Export] public float Acceleration { get; set; } = 1200f;
    [Export] public float Friction { get; set; } = 1200f;
    [Export] public float NetSmoothing { get; set; } = 12f;

    private Vector2 _netTargetPos;
    private Vector2 _netTargetVel;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (IsMultiplayerAuthority())
        {
            // Local authority: compute movement from input
            Vector2 input = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
            Vector2 targetVelocity = input * Speed;

            Vector2 v = Velocity;
            if (input != Vector2.Zero)
            {
                v = v.MoveToward(targetVelocity, Acceleration * dt);
            }
            else
            {
                v = v.MoveToward(Vector2.Zero, Friction * dt);
            }

            Velocity = v;
            MoveAndSlide();

            // Send state to others (unreliable via attribute on ReceiveState)
            Rpc(nameof(ReceiveState), GlobalPosition, Velocity);
        }
        else
        {
            // Remote proxy: smoothly converge to networked state
            GlobalPosition = GlobalPosition.Lerp(_netTargetPos, 1f - Mathf.Exp(-NetSmoothing * dt));
            Velocity = Velocity.Lerp(_netTargetVel, 1f - Mathf.Exp(-NetSmoothing * dt));
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ReceiveState(Vector2 pos, Vector2 vel)
    {
        // Ignore our own authoritative updates
        if (IsMultiplayerAuthority()) return;
        _netTargetPos = pos;
        _netTargetVel = vel;
    }
}
