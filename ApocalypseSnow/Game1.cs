using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ApocalypseSnow;
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
public class Game1 : Game
{
    private GraphicsDeviceManager _graphics;

    // Risorse grafiche principali
    private Texture2D _backgroundTexture = null!;
    private SpriteBatch _spriteBatch = null!;

    // Risoluzione finestra
    private const int ScreenWidth = 800;
    private const int ScreenHeight = 600;

    // Percorsi contenuti
    private const string ContentDirectory = "Content";
    private const string BackgroundImagePath = ContentDirectory + "/images/environment.png";

    // Sessione di gioco attiva (contiene stato e logica)
    private GameSession     _currentSession = null!;

    /// Costruttore:
    /// - Configura dimensioni finestra.
    /// - Imposta directory contenuti.
    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        _graphics.PreferredBackBufferWidth = ScreenWidth;
        _graphics.PreferredBackBufferHeight = ScreenHeight;

        Content.RootDirectory = ContentDirectory;
        IsMouseVisible = true;
    }

    /// Fase di inizializzazione logica.
    /// Qui creiamo la GameSession e colleghiamo gli eventi di spawn/despawn.
    protected override void Initialize()
    {
        _currentSession = new GameSession(this);
        _currentSession.OnEntitySpawned += entity => Components.Add(entity);
        _currentSession.OnEntityDestroyed += entity => Components.Remove(entity);

        Components.Add(_currentSession);

        // AGGIUNGI QUESTA RIGA: Collega l'HUD al framework
        Components.Add(new HUD(this, _currentSession));

        base.Initialize();
        _currentSession.Start();
    }

    /// Caricamento delle risorse grafiche (texture, font).
    /// Viene chiamato una sola volta all'avvio.
    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        // Caricamento background da file
        using var stream = System.IO.File.OpenRead(BackgroundImagePath);
        _backgroundTexture = Texture2D.FromStream(GraphicsDevice, stream);

        base.LoadContent();
    }

    /// Update globale del gioco.
    /// Non contiene logica diretta:
    /// delega tutto ai GameComponent registrati (Penguin, Ball, ecc.).
    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
    }

    /// Pipeline di rendering:
    /// 1) Pulizia schermo
    /// 2) Disegno background
    /// 3) Disegno GameComponents (base.Draw)
    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.White);

        _spriteBatch.Begin();
        _spriteBatch.Draw(_backgroundTexture,
            new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
            Color.White);
        _spriteBatch.End();

        // Disegna automaticamente l'HUD e gli altri componenti
        base.Draw(gameTime);
    }

    /// Pulizia risorse.
    /// Viene chiamato alla chiusura del gioco.
    protected override void UnloadContent()
    {
        _currentSession?.Dispose();
        _backgroundTexture?.Dispose();
        _spriteBatch?.Dispose();
        base.UnloadContent();
    }
}