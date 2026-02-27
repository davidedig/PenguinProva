using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharpDX.Direct2D1.Effects;

namespace ApocalypseSnow;

public class Obstacle:DrawableGameComponent
{
    private Texture2D _texture;
    private readonly string _tag;
    private Rectangle _sourceRect;
    private readonly Vector2 _position;
    private readonly int _posX;
    private readonly int _posY;
    private int _textureFractionWidth;
    private int _textureFractionHeight;
    private int _halfTextureFractionWidth;
    private int _halfTextureFractionHeight;

    private SpriteBatch _spriteBatch;

    public Obstacle(Game game, Vector2 position, int posX, int posY) : base(game)
    {
        _position = position;
        _posX = posX;
        _posY = posY;
        _tag = "obstacle";
    }

    private Vector2 GetPosition(int x, int y)
    {
        float posX = x * (_textureFractionWidth);
        float posY = y * (_textureFractionHeight);
        Vector2 pos = new Vector2(posX, posY);
        return pos;
    }
    
    private void load_texture(GraphicsDevice gd, string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        // 1. Carichiamo l'immagine (deve essere nel Content Pipeline)
        _texture = Texture2D.FromStream(gd, stream);
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        //Vector2 position = GetPosition(_posX,  _posY);
        load_texture(GraphicsDevice, "Content/images/ostacoli1.png");
        Vector2 position = GetPosition(_posX,  _posY);
        _textureFractionWidth = _texture.Width / 2;
        _textureFractionHeight = _texture.Height / 2;
        _halfTextureFractionWidth = _textureFractionWidth / 2;
        _halfTextureFractionHeight = _textureFractionHeight / 2;
        int posCollX = (int)_position.X + _halfTextureFractionWidth;
        int posCollY = (int)_position.Y + _halfTextureFractionHeight;
        _sourceRect = new Rectangle((int)position.X, (int)position.Y, _textureFractionWidth, _textureFractionHeight);
        CollisionManager.Instance.addObject(_tag, posCollX, posCollY, _textureFractionWidth, _textureFractionHeight);
    }

    public override void Draw(GameTime gameTime)
    {
        _spriteBatch.Begin();

        _spriteBatch.Draw(_texture, _position, _sourceRect, Color.White);

        _spriteBatch.End();
    }
}