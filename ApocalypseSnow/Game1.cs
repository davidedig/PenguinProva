using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace ApocalypseSnow;

/// <summary>
/// Entry point principale del client MonoGame.
/// 
/// Responsabilità:
/// - Configurare finestra e rendering.
/// - Creare e gestire la GameSession (logica di gioco).
/// - Disegnare background e HUD.
/// - Delegare Update/Draw ai GameComponents registrati.
/// 
/// Non contiene logica di gameplay.
/// La logica vive dentro GameSession e nei vari Component (Penguin, Ball, ecc.).
/// </summary>
public class Game1 : Game
{
    private GraphicsDeviceManager _graphics;

    // Risorse grafiche principali
    private Texture2D _backgroundTexture = null!;
    private SpriteBatch _spriteBatch = null!;
    private SpriteFont _uiFont = null!;

    // Risoluzione finestra
    private const int ScreenWidth = 640;
    private const int ScreenHeight = 480;

    // Percorsi contenuti
    private const string ContentDirectory = "Content";
    private const string BackgroundImagePath = ContentDirectory + "/images/environment.png";
    private const string UiSpriteFont = "UIAmmo";

    // Sessione di gioco attiva (contiene stato e logica)
    private GameSession _currentSession = null!;

    /// <summary>
    /// Costruttore:
    /// - Configura dimensioni finestra.
    /// - Imposta directory contenuti.
    /// </summary>
    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        _graphics.PreferredBackBufferWidth = ScreenWidth;
        _graphics.PreferredBackBufferHeight = ScreenHeight;

        Content.RootDirectory = ContentDirectory;
        IsMouseVisible = true;
    }

    /// <summary>
    /// Fase di inizializzazione logica.
    /// Qui creiamo la GameSession e colleghiamo gli eventi di spawn/despawn.
    /// </summary>
    protected override void Initialize()
    {
        _currentSession = new GameSession(this);

        _currentSession.OnEntitySpawned += entity => Components.Add(entity);
        _currentSession.OnEntityDestroyed += entity => Components.Remove(entity);

        Components.Add(_currentSession);

        base.Initialize();

        _currentSession.Start();
    }

    /// <summary>
    /// Caricamento delle risorse grafiche (texture, font).
    /// Viene chiamato una sola volta all'avvio.
    /// </summary>
    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _uiFont = Content.Load<SpriteFont>(UiSpriteFont);

        // Caricamento background da file
        using var stream = System.IO.File.OpenRead(BackgroundImagePath);
        _backgroundTexture = Texture2D.FromStream(GraphicsDevice, stream);

        base.LoadContent();
    }

    /// <summary>
    /// Update globale del gioco.
    /// Non contiene logica diretta:
    /// delega tutto ai GameComponent registrati (Penguin, Ball, ecc.).
    /// </summary>
    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
    }

    /// <summary>
    /// Pipeline di rendering:
    /// 1) Pulizia schermo
    /// 2) Disegno background
    /// 3) Disegno GameComponents (base.Draw)
    /// 4) Disegno HUD sopra tutto
    /// </summary>
    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.White);

        // --- BACKGROUND ---
        _spriteBatch.Begin();
        _spriteBatch.Draw(
            _backgroundTexture,
            new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
            Color.White
        );
        _spriteBatch.End();

        // --- ENTITÀ DI GIOCO ---
        // Disegna tutti i DrawableGameComponents registrati
        base.Draw(gameTime);

        // --- HUD ---
        _spriteBatch.Begin();

        string ammoText = $"Munizioni: {_currentSession.LocalPlayerAmmo}";
        _spriteBatch.DrawString(
            _uiFont,
            ammoText,
            new Vector2(GraphicsDevice.Viewport.Width / 10f, GraphicsDevice.Viewport.Height / 1.2f),
            Color.Black
        );

        _spriteBatch.End();
    }

    /// <summary>
    /// Pulizia risorse.
    /// Viene chiamato alla chiusura del gioco.
    /// </summary>
    protected override void UnloadContent()
    {
        _currentSession?.Dispose();
        _backgroundTexture?.Dispose();
        _spriteBatch?.Dispose();
        base.UnloadContent();
    }
}