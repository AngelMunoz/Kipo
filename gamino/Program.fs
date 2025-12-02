module Gamino.Core

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Input

// ==========================================
// 1. Domain Types
// ==========================================

/// Represents the state of the camera in our isometric world.
type IsometricCamera = { View: Matrix; Projection: Matrix }

/// Holds all the assets we've loaded.
type GameAssets = { Models: Map<string, Model> }

/// The entire state of our game world at any given frame.
type GameState =
    { Position: Vector2
      Rotation: float32
      Camera: IsometricCamera }

// ==========================================
// 2. Constants & Configuration
// ==========================================

[<Literal>]
let AssetRoot = "Content"

[<Literal>]
let AssetPathPrefix = "KayKit_Prototype_Bits_1.1_FREE/Assets/obj/"

let DummyParts =
    [ "Dummy_Base"
      "Dummy_Base_Dummy_Body"
      "Dummy_Base_Dummy_Body_Dummy_ArmLeft"
      "Dummy_Base_Dummy_Body_Dummy_ArmRight"
      "Dummy_Base_Dummy_Body_Dummy_Head"
      "Dummy_Base_Dummy_Body_Dummy_Target" ]

// ==========================================
// 3. Initialization Logic
// ==========================================

module Initialization =

    /// Creates the initial camera setup for an isometric orthographic view.
    let createCamera (device: GraphicsDevice) =
        // View: Classic isometric angle (looking from 20,20,20 towards origin)
        let isoPosition = Vector3(20.0f, 20.0f, 20.0f)
        let view = Matrix.CreateLookAt(isoPosition, Vector3.Zero, Vector3.Up)

        // Projection: Orthographic to maintain parallel lines (no perspective distortion)
        let viewWidth = 25.0f
        let viewHeight = viewWidth / device.Viewport.AspectRatio
        let projection = Matrix.CreateOrthographic(viewWidth, viewHeight, 0.1f, 1000.0f)

        { View = view; Projection = projection }

    let private tryLoadModel (content: ContentManager) (name: string) (path: string) =
        try
            Some(name, content.Load<Model> path)
        with ex ->
            printfn "Failed to load asset '%s': %s" name ex.Message
            None

    /// Loads all required game assets.
    let loadAssets (content: ContentManager) =
        let models = ResizeArray()

        // 1. Load Dummy Parts
        for name in DummyParts do
            tryLoadModel content name (AssetPathPrefix + name) |> Option.iter models.Add

        // 2. Load Barrel
        tryLoadModel content "Barrel" (AssetPathPrefix + "Barrel_A")
        |> Option.iter models.Add

        let allModels = models |> Map.ofSeq

        { Models = allModels }

// ==========================================
// 4. Update Logic
// ==========================================

module Update =

    /// Returns the new game state based on input and elapsed time.
    /// In a pure functional architecture, this takes State -> State.
    let updateState
        (gameTime: GameTime)
        (currentInput: KeyboardState)
        (gamePad: GamePadState)
        (currentState: GameState)
        =

        // Check for exit conditions (handled by the wrapper usually, but logic belongs here)
        let shouldExit =
            gamePad.Buttons.Back = ButtonState.Pressed || currentInput.IsKeyDown Keys.Escape

        // Movement Logic
        let speed = 5.0f * float32 gameTime.ElapsedGameTime.TotalSeconds
        let mutable inputVelocity = Vector2.Zero

        if currentInput.IsKeyDown Keys.Up then inputVelocity.Y <- inputVelocity.Y - 1.0f
        if currentInput.IsKeyDown Keys.Down then inputVelocity.Y <- inputVelocity.Y + 1.0f
        if currentInput.IsKeyDown Keys.Left then inputVelocity.X <- inputVelocity.X - 1.0f
        if currentInput.IsKeyDown Keys.Right then inputVelocity.X <- inputVelocity.X + 1.0f

        let newPosition, newRotation =
            if inputVelocity <> Vector2.Zero then
                inputVelocity <- Vector2.Normalize inputVelocity
                
                // Transform 2D Input -> Isometric World 3D Motion
                // Screen Up (Input Y-) corresponds to World (-X, -Z)
                // Screen Right (Input X+) corresponds to World (+X, -Z)
                // Formula: 
                // WorldX = InputX + InputY
                // WorldZ = InputY - InputX
                
                let worldDx = inputVelocity.X + inputVelocity.Y
                let worldDz = inputVelocity.Y - inputVelocity.X
                
                // Normalize the world velocity to maintain constant speed
                let worldVelocity = Vector2(worldDx, worldDz) |> Vector2.Normalize

                // Calculate Angle: Atan2(x, z)
                let angle = float32 (System.Math.Atan2(float worldVelocity.X, float worldVelocity.Y))

                (currentState.Position + worldVelocity * speed, angle)
            else
                (currentState.Position, currentState.Rotation)

        // Return a tuple of (ShouldExit, NewState)
        let nextState =
            { currentState with
                Position = newPosition
                Rotation = newRotation }

        shouldExit, nextState

// ==========================================
// 5. Drawing Logic
// ==========================================

module Rendering =

    /// Configures the BasicEffect for a mesh part with our standard lighting.
    let private configureEffect (camera: IsometricCamera) (world: Matrix) (effect: BasicEffect) =
        effect.EnableDefaultLighting()
        effect.PreferPerPixelLighting <- true

        // Lighting Setup
        effect.AmbientLightColor <- Vector3(0.5f, 0.5f, 0.5f)
        effect.DirectionalLight0.Direction <- Vector3(1.0f, -1.0f, -1.0f)
        effect.DirectionalLight0.DiffuseColor <- Vector3(0.8f, 0.8f, 0.8f)
        effect.DirectionalLight0.SpecularColor <- Vector3(0.2f, 0.2f, 0.2f)

        // Matrices
        effect.World <- world
        effect.View <- camera.View
        effect.Projection <- camera.Projection

    /// Draws a single model at a given world matrix.
    let private drawModel (camera: IsometricCamera) (model: Model) (world: Matrix) =
        for mesh in model.Meshes do
            for effect in mesh.Effects do
                configureEffect camera world (effect :?> BasicEffect)

            mesh.Draw()

    /// The main render loop.
    let drawScene (device: GraphicsDevice) (state: GameState) (assets: GameAssets) =
        device.Clear Color.CornflowerBlue

        // Important: Enable Depth Buffer for 3D rendering
        device.DepthStencilState <- DepthStencilState.Default
        device.SamplerStates.[0] <- SamplerState.LinearWrap
        // Enable MSAA on rasterizer (backbuffer MSAA is configured via GraphicsDeviceManager)
        device.RasterizerState <- new RasterizerState(MultiSampleAntiAlias = true)

        // 1. Draw Background Row (Separated Parts)
        let startX = -5.0f
        let spacing = 2.0f

        DummyParts
        |> List.iteri (fun i name ->
            match assets.Models.TryFind name with
            | Some model ->
                let xPos = startX + float32 i * spacing
                let position = Matrix.CreateTranslation(Vector3(xPos, 0.0f, -3.0f))
                drawModel state.Camera model position
            | None -> ())

        // 2. Draw Assembled Dummy (Foreground, Movable)
        // We map 2D State.Position (X, Y) -> 3D World (X, 0, Z)
        let assembledPosition =
            Matrix.CreateRotationY state.Rotation
            * Matrix.CreateTranslation(Vector3(state.Position.X, 0.0f, state.Position.Y))

        DummyParts
        |> List.iter (fun name ->
            match assets.Models.TryFind name with
            | Some model -> drawModel state.Camera model assembledPosition
            | None -> ())

        // 3. Draw Barrel (Far Left)
        match assets.Models.TryFind "Barrel" with
        | Some barrel ->
            let barrelPos =
                Matrix.CreateTranslation(Vector3(-6.0f, 0.0f, 3.0f))

            drawModel state.Camera barrel barrelPos
        | None -> ()


// ==========================================
// 6. The Game Wrapper (Infrastructure)
// ==========================================

type GaminoGame() as this =
    inherit Game()

    let graphics = new GraphicsDeviceManager(this)

    let mutable assets: GameAssets option = None

    // Mutable state container
    let mutable state =
        { Position = Vector2(0.0f, 3.0f)
          Rotation = 0.0f
          Camera =
            { View = Matrix.Identity
              Projection = Matrix.Identity } }

    do
        this.Content.RootDirectory <- AssetRoot
        this.IsMouseVisible <- true
        this.Window.AllowUserResizing <- true

    override this.Initialize() =
        this.Services.AddService graphics
        // Initialize logic-based state (Camera) using the device
        state <-
            { state with
                Camera = Initialization.createCamera this.GraphicsDevice }

        base.Initialize()

    override this.LoadContent() =
        // Load immutable assets
        assets <- Some(Initialization.loadAssets this.Content)

    override this.Update gameTime =
        let keyboard = Keyboard.GetState()
        let gamepad = GamePad.GetState PlayerIndex.One

        // Run pure update logic
        let shouldExit, newState = Update.updateState gameTime keyboard gamepad state

        // Update mutable shell
        state <- newState

        if shouldExit then
            this.Exit()

        base.Update gameTime

    override this.Draw gameTime =
        assets |> Option.iter (Rendering.drawScene this.GraphicsDevice state)

        base.Draw gameTime

[<EntryPoint>]
let main argv =
    use game = new GaminoGame()
    game.Run()
    0
