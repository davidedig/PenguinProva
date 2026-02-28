using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ApocalypseSnow; // Assicurati che il namespace sia corretto

public sealed class HUD : DrawableGameComponent
{
    private const string FontName = "UIAmmo";
    private readonly GameSession _session;
    private SpriteBatch _spriteBatch = null!;
    private SpriteFont _font = null!;

    public HUD(Game game, GameSession session) : base(game)
    {
        _session = session;
        DrawOrder = 9999; // Resta sopra tutto
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _font = Game.Content.Load<SpriteFont>(FontName);
        base.LoadContent();
    }

    public override void Draw(GameTime gameTime)
    {
        _spriteBatch.Begin();
        var viewport = GraphicsDevice.Viewport;

        // Disegna qui tutte le info che vuoi
        string ammoText = $"Munizioni: {_session.LocalPlayerAmmo}";
        _spriteBatch.DrawString(_font, ammoText,
            new Vector2(viewport.Width * 0.05f, viewport.Height * 0.85f), Color.Black);

        _spriteBatch.End();
    }
}