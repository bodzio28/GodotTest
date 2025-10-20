using Godot;

public partial class Player2 : CharacterBody2D
{
    [Export] public float Speed { get; set; } = 220f;
    [Export] public float Acceleration { get; set; } = 1200f;
    [Export] public float Friction { get; set; } = 1200f;

    public override void _PhysicsProcess(double delta)
    {
        // Read normalized input from the InputMap (default: ui_left/right/up/down)
        Vector2 input = Input.GetVector("move_left", "move_right", "move_up", "move_down");
        Vector2 targetVelocity = input * Speed;

        float dt = (float)delta;
        Vector2 v = Velocity;

        if (input != Vector2.Zero)
        {
            // Accelerate toward target velocity when moving
            v = v.MoveToward(targetVelocity, Acceleration * dt);
        }
        else
        {
            // Apply friction when no input
            v = v.MoveToward(Vector2.Zero, Friction * dt);
        }

        Velocity = v;
        MoveAndSlide();
    }
}
