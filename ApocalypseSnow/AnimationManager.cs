using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace ApocalypseSnow;

public class AnimationManager
{
    public Texture2D Texture { get; private set; } = null!;

    // 0: walking
    // 1: walking_snowball
    // 2: reloading (gathering)
    // 3: shooting (launch)
    private readonly Texture2D[] _textures = new Texture2D[4];

    private const int Columns = 3;
    private const int Rows = 4;

    private const float FrameSpeed = 0.1f;
    private float _tempTime;
    private int _currentFrame;

    private Rectangle _sourceRect;
    public Rectangle SourceRect => _sourceRect;

    public Texture2D this[int index] => _textures[index];

    private string color;

    public AnimationManager(string color)
    {
        this.color = color;
    }

    private void LoadTexture(GraphicsDevice gd, int index, string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        _textures[index] = Texture2D.FromStream(gd, stream);
    }

    public void Load_Content(GraphicsDevice graphicsDevice)
    {
        LoadTexture(graphicsDevice, 0, "Content/images/penguin_"+color+"_walking.png");
        LoadTexture(graphicsDevice, 1, "Content/images/penguin_" + color + "_walking_snowball.png");
        LoadTexture(graphicsDevice, 2, "Content/images/penguin_"+color+"_gathering.png");
        LoadTexture(graphicsDevice, 3, "Content/images/penguin_"+ color +"_launch.png");

        Texture = _textures[1];

        int frameW = Texture.Width / Columns;
        int frameH = Texture.Height / Rows;

        _sourceRect = new Rectangle(frameW, 0, frameW, frameH); // idle centrale (col=1)
        _currentFrame = 1;
        _tempTime = 0f;
    }

    private static bool HasMoveInput(StateList input)
    {
        StateList m = input & (StateList.Up | StateList.Down | StateList.Left | StateList.Right);

        if ((m & StateList.Left) != 0 && (m & StateList.Right) != 0)
            m &= ~(StateList.Left | StateList.Right);

        if ((m & StateList.Up) != 0 && (m & StateList.Down) != 0)
            m &= ~(StateList.Up | StateList.Down);

        return m != StateList.None;
    }

    private void SelectTexture(PenguinState anim)
    {
        Texture = anim switch
        {
            PenguinState.Walking => _textures[0],
            PenguinState.WalkingSnowball => _textures[1],
            PenguinState.Reloading => _textures[2],
            PenguinState.Shooting => _textures[3],
            PenguinState.WalkingEgg => _textures[0], // placeholder
            _ => _textures[1],
        };

        // aggiorna SEMPRE dimensioni frame (width+height) quando cambia texture
        _sourceRect.Width = Texture.Width / Columns;
        _sourceRect.Height = Texture.Height / Rows;
    }

    private void Animate3Frames(float dt, bool animate)
    {
        if (animate)
        {
            _tempTime += dt;
            if (_tempTime > FrameSpeed)
            {
                _currentFrame = (_currentFrame + 1) % Columns; // 0..2
                _tempTime = 0f;
            }
        }
        else
        {
            _currentFrame = 1; // idle centrale
        }

        _sourceRect.X = _currentFrame * _sourceRect.Width;
    }

    // stato + input raw
    public void Update(float dt, PenguinState anim, StateList input)
    {
        SelectTexture(anim);

        bool animate = anim == PenguinState.Reloading
                    || anim == PenguinState.Shooting
                    || HasMoveInput(input);

        Animate3Frames(dt, animate);
    }

    public void Draw(SpriteBatch spriteBatch, Vector2 position)
    {
        spriteBatch.Draw(Texture, position, _sourceRect, Color.White);
    }

    // rowIndex: 0..3 (4 righe)
    public void SetRow(int rowIndex)
    {
        rowIndex = Math.Clamp(rowIndex, 0, Rows - 1);
        _sourceRect.Y = rowIndex * _sourceRect.Height;
    }
}