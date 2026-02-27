using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Runtime.InteropServices;

namespace ApocalypseSnow;

public class Ball: DrawableGameComponent
{
    private Texture2D _texture;
    private string _tag;
    private readonly Vector2 _startPosition;
    private Vector2 _position;
    private readonly Vector2 _startSpeed;
    private float _ballTime;
    private readonly Vector2 _finalPosition;
    private float _scale;
    private static readonly float Gravity = 150f;
    private static readonly float L = 0.1f;  // massimo
    private static readonly float K = 0.0003f;   // velocità di crescita
    private int _halfTextureFractionWidth;
    private int _halfTextureFractionHeight;

    private SpriteBatch _spriteBatch;
    public Ball(Game game, Vector2 startPosition, Vector2 startSpeed, Vector2 finalPosition, string tag) : base(game)
    {
        this._startPosition = startPosition;
        this._position = startPosition;
        this._startSpeed = startSpeed;
        _ballTime = 0.0f;
        this._finalPosition = finalPosition;
        _tag = tag;
        this._scale = 1.0f;
        
    }


    private void FinalPointCalculous()
    {
        bool haRaggiuntoTarget = false;
        float differenceY = Math.Abs(_startPosition.Y-_finalPosition.Y);
        //Console.WriteLine(differenceY);
        
        float t = differenceY; // es: tempo, velocità, carica

        float x = L * (1f - MathF.Exp(-K * t));
        int f = (int)((_finalPosition.X+_startPosition.X+20)/2);
        
        switch (_startSpeed.X)
        {
            // Tiro verso DESTRA
            case > 0:
            {
                if (_position.X >= _finalPosition.X) haRaggiuntoTarget = true;
                if (_position.X < f) { _scale = _scale + x; }
                else { _scale = _scale - x; }

                break;
            }
            // Tiro verso SINISTRA
            case < 0:
            {
                if (_position.X <= _finalPosition.X) haRaggiuntoTarget = true;
                if (_position.X > f) { _scale = _scale + x; }
                else { _scale = _scale - x; }

                break;
            }
        }

        // 3. Applichiamo l'impatto se il target è raggiunto
        if (haRaggiuntoTarget)
        {
            // Rimuoviamo la palla
            Game.Components.Remove(this);
            CollisionManager.Instance.removeObject(_tag);
            // ball_list.Remove(this); // Se hai passato il riferimento alla lista
        }
    }
    

    private void load_texture(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        // 1. Carichiamo l'immagine (deve essere nel Content Pipeline)
        this._texture = Texture2D.FromStream(GraphicsDevice, stream);
    }

    [DllImport("libPhysicsDll.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void parabolic_motion(float gravity, float startPositionX, float startPositionY, ref float positionX, ref float positionY, float startVelocityX,
        float startVelocityY, float gameTime);
    
    
    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        load_texture("Content/images/palla1.png");
        CollisionManager.Instance.addObject(_tag, _position.X, _position.Y, _texture.Width, _texture.Height );
        _halfTextureFractionWidth  = _texture.Width / 2;
        _halfTextureFractionHeight = _texture.Height / 2;
    }


    /* public void Draw(SpriteBatch spriteBatch)
     {
         spriteBatch.Draw(_texture, _position,null, Color.White, 0f, 
             Vector2.Zero, 
             _scale, 
             SpriteEffects.None, 
             0f);
     }

     */

    public override void Draw(GameTime gameTime)
    {
        _spriteBatch.Begin();

        _spriteBatch.Draw(_texture, _position, null, Color.White, 0f,
            Vector2.Zero,
            _scale,
            SpriteEffects.None,
            0f);

        _spriteBatch.End();
    }

    public override void Update(GameTime gameTime)
    {
        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _ballTime += deltaTime;
        
        parabolic_motion(Gravity,_startPosition.X+20, _startPosition.Y, ref _position.X, ref _position.Y,_startSpeed.X, -_startSpeed.Y, _ballTime);
        
        //Console.WriteLine($"Campo: {v._x}, Valore: {v._y}");
        //Console.WriteLine($"Scale: {_scale}");
        //Console.WriteLine($"Gravity: {gravity}");
        if (_scale < 1.0f) { _scale = 1.0f; }
        if (_scale > 1.6f) { _scale = 1.6f; }
        FinalPointCalculous();
        int posCollX = (int)_position.X + _halfTextureFractionWidth;
        int posCollY = (int)_position.Y + _halfTextureFractionHeight;
        //Console.WriteLine($"posCollX: {posCollX}, posCollY: {posCollY}");
        CollisionManager.Instance.modifyObject(_tag, posCollX, posCollY, _texture.Width, _texture.Height );
       
    }
    
}