# Prompt de continuación — Laboratorio virtual de Inmunología (InmunologiaV2)

> Copia todo este documento como primer mensaje de un chat nuevo.

---

Hola. Vamos a continuar el desarrollo de mi juego educativo en Unity. Antes de hacer cualquier cambio, **conéctate a Unity (herramientas `mcp__remote-devices__unity__*`), revisa el estado actual del proyecto y confirma que entendiste este contexto**. Trabajamos en **español**. Me llamo Diego (UNACH).

## 1. Proyecto

- **Juego:** "Laboratorio virtual de Inmunología". Es en primera persona y tiene fines educativos (prácticas de inmunología).
- **Motor:** Unity **6000.6.3f1**, **URP**, solo el **Input System nuevo** (`Keyboard.current` / `Mouse.current`, con `#if ENABLE_INPUT_SYSTEM`).
- **Ruta:** `C:\Users\zicko\OneDrive\Documentos\Unity\InmunologiaV2`
- **Escena única:** `Assets/Scenes/SampleScene.unity`. Los 3 niveles viven en la misma escena; `LevelDirector` enciende o apaga los objetos de cada nivel.
- **PlayerSettings:** productName `InmunologiaV2`, company `DefaultCompany`, versión 0.1.0.
- **Base de datos SQLite:** `C:/Users/zicko/AppData/LocalLow/DefaultCompany/InmunologiaV2/inmunologia.db`
  - Tablas: `alumnos`, `conejos`, `pesajes`, `resultados`.
  - En `alumnos` hay 1 registro real ("Dadadada Dadada · ADADADAD"). **No borrarlo.**
  - Los resultados de prueba que generes sí los puedes borrar.
- **Build de Windows:**
  - Ejecutable: `Builds/Windows/InmunologiaV2.exe`
  - Zip: `Builds/InmunologiaV2_Windows.zip` (~59.5 MB, carpeta interna `InmunologiaV2/`, sin `*DoNotShip*` ni `BackUpThisFolder`).
  - **Ya está actualizado con todo lo de este documento.** Si cambias algo, recompila el .exe y regenera el zip.
- **Créditos (menú principal):**
  - Desarrollo del juego en Unity: **Diego Pineda Gumeta**
  - Modelado 3D en Blender: **Juan de Dios Alvarado Morga**
  - Lógica de programación: **Martin Emmanuel Erants Solorzano**

## 2. Reglas de trabajo (importante)

1. **Toda corrección se verifica en Play Mode** con capturas y mediciones, no solo razonando.
2. Al terminar:
   - consola sin errores;
   - borrar los objetos temporales (`CamTmp`, `LuzTmp`, `Tmp*`);
   - **guardar la escena**.
3. Si una decisión de diseño es del usuario (mecánica, tecla, aspecto), **pregunta antes** con opciones.
4. Respuestas concisas en español: qué cambió, cómo se verificó y qué decisiones tomaste.

## 3. Trucos técnicos de la conexión con Unity (ahorran mucho tiempo)

- **Timeout de `eval` (5 s en el hilo principal):** usa el parámetro `timeout` (p. ej. 30000–400000) en operaciones largas.
- **Si aparece "Main thread operation timed out":** llama a `set_autotick` y/o `editor_focus`, espera y reintenta. A veces el servidor MCP se desconecta un momento; `RefreshMcpTools` lo reconecta.
- **Errores de compilación viejos:** a veces la consola muestra errores de una compilación anterior. Confirma con `editor_status` y revisando por reflexión que el tipo o miembro exista.
- **Play Mode no avanza solo.** Patrón:
  1. `editor_play`
  2. `editor_pause`
  3. avanzar con `UnityEditor.EditorApplication.Step()` en ciclos dentro de `eval`.
- **Scripts largos:**
  - escríbelos en `Temp/*.cs` (con `System.IO.File.WriteAllText` desde `eval`) y ejecútalos con `eval_file`;
  - para pasar parámetros entre llamadas usa `UnityEditor.SessionState`;
  - ya hay helpers en `Temp/`, por ejemplo:
    - `t_play.cs`: arranca el nivel `SessionState tLevel` en el modo `tMode` (0 educativo / 1 evaluación);
    - `t_rabbit.cs` y `t_pet.cs`: pruebas del conejo.
- **Escribir o editar archivos:**
  - `write_text_file` (con `confirm:true` para sobrescribir) escribe scripts directamente en el proyecto;
  - para ediciones puntuales usa `eval` con `File.ReadAllText` + `Replace` + `WriteAllText`, verificando que el texto buscado aparezca exactamente 1 vez;
  - después, `recompile`.
- **Capturas:**
  - `capture_game_view` puede devolver una imagen cacheada: haz `Repaint` de la GameView + varios `Step()` antes de capturar;
  - para ángulos libres crea una cámara temporal `CamTmp` (deshabilitada) y captura con `source=camera`;
  - no uses `save_path` (crea archivos dentro de Assets).
- **Simular entrada:**
  - `InputState.Change(Mouse.current, new MouseState{delta=...}.WithButton(MouseButton.Right,true))`;
  - `InputState.Change` por botón individual falla; `QueueStateEvent` no es confiable.
  - Para acciones de E/R es más fiable invocar `Interactable.PromptFor/Interact`, `ObjectGrabber.Grab/Release` o los métodos del manager. Antes, teletransporta al jugador y apunta la cámara: campo privado `pitch` de `PlayerController` por reflexión y `cameraTransform.localRotation`.
  - Si teletransportas o giras al jugador con el conejo en brazos, el conejo se estresa.
- **Valores serializados:** los componentes conservan valores viejos aunque cambies el default en el código. Actualiza la escena o el prefab explícitamente y guarda.
- **Variables estáticas:** se reinician con `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` porque no hay recarga de dominio.
- **Puente de archivos con mi PC:**
  - solo está conectada la carpeta `Assets\objetos`;
  - para pasar binarios: copia a `/mnt/user-data/outputs` y luego `device_commit_files`;
  - para leer PNG: `device_stage_files`;
  - `device_bash` no está disponible.
- **Advertencias conocidas e inofensivas:** `MaterialLocation.External is obsolete`, colisión pre-bakeada, `ConvGeneric` y shaders de Sentis en el build, Convex Mesh de `florence_flask`.

## 4. Arquitectura general

- **Flujo (`GameFlow`):**
  - Estados: Menu, Transition, Playing, Paused, Results, Minigame.
  - Métodos: `EnterMinigame/ExitMinigame`, `ShowResults`, `RestartLevel`, `ContinueToNextLevel`, `GoToMainMenu`.
  - `GameFlow.GameplayInputAllowed`.
- **`GameSession`:** `Level` (1–3), `Mode` (Educativo / Evaluacion), `MaxLevel = 3`, `StartLevelDirectly`.
- **`LevelDirector`** (orden de ejecución −1000):
  - `levels[]`: controller (`ILevelController`), objetos del nivel y textos del pizarrón;
  - `level1Props` (Mat_N1, Mat_incorrectos) quedan "congelados" como decoración fuera del nivel 1;
  - mueve la báscula para el nivel 2.
- **Menú (`MainMenuUI`):**
  - Jugar → formulario del alumno (`StudentFormUI`, `StudentProfile`, SQLite) → selección de nivel (botones Nivel1/2/3) y modo.
  - También: Opciones, Créditos y Salir.
- **Interacción (`ObjectGrabber`, en la cámara):**
  - apuntar + E o clic = agarrar/soltar o interactuar; R = acción secundaria;
  - clic derecho = rotar lo que sostienes; rueda = acercar/alejar;
  - `Grabbable` (displayName, `GrabBlocker`, `ReleaseBlocker`, `HeldPrompt`, eventos Grabbed/Released, `customCarry`, `holdViewOffset`);
  - `Interactable` (`PromptFor`/`Interact`/`InfoFor`);
  - `GrabHitbox` (trigger extra para objetos delgados);
  - `ObjectGrabber.MessageSink` / `AimHint`;
  - `SetReceiving` (bloquea mientras un objeto vuela a la mano).
- **Mira:** cruz blanca con contorno; crece y se pone verde al apuntar algo interactuable.
- **Jugador:** `PlayerController` (WASD, Shift, Espacio, `lookEnabled`, `SpeedMultiplier`) y `CameraView` (V alterna 1ª/3ª persona, `AimRay`).
- **Avatar:**
  - **Prefabs y animaciones:** prefabs humanoides `Avatar_Hombre` y `Avatar_Mujer` (`Assets/Prefabs/Avatares`, modelos en `Assets/objetos/Avatares/*.fbx`, **Read/Write activado**). Animaciones procedurales horneadas con `Assets/Editor/AvatarBuilder.cs`.
  - **`PlayerAvatar`:**
    - el avatar va en la capa "Avatar" (6), que la cámara de los ojos no dibuja; sí lo dibujan el espejo y la cámara de 3ª persona;
    - crea copias de solo sombra;
    - `SetGloves(bool)`.
  - **`AvatarAnimatorDriver`:**
    - controla locomoción, mirada (look-at), IK de manos hacia lo sostenido y saludo;
    - emotes en la capa 2;
    - `cullingMode = AlwaysAnimate` para el jugador;
    - IK de acariciar (`PetWeight`, `palmSign`, `palmCenter`, `palmLift`).
  - **`AvatarHands`:**
    - separa las manos de la piel (submesh nuevo) para los guantes;
    - crea `BrazosPrimeraPersona` (antebrazos + manos en la capa Default), visibles solo mientras se acaricia al conejo en 1ª persona.
- **Espejo:** `PlanarMirror` y `MirrorInteractable` (E = saludar).
- **Otros:**
  - F1 muestra el contador de FPS;
  - Esc abre la pausa (con ayuda de teclas: B emotes, V cámara);
  - `EmoteWheel`: mantener B → Saludo, Six Seven, Passinho do Jamal y "Baile de Homero" (este último pendiente; ver §7).

## 5. Niveles

### Nivel 1 · Preparación y esterilización del material (`Level1Manager`)

- **Material correcto (`Mat_N1`):** matraz Erlenmeyer, Micropipeta (modelo `Micropipeta.fbx`, con hitbox ampliada), frasco de solución salina, jeringa y gradilla.
- **Distractores:** `Mat_incorrectos`.
- **Aparición:** `MaterialSpawner` coloca el material al azar en lugares coherentes: repisa bajo el pizarrón y estante junto a la mesa (arriba / en medio / abajo).
- **Esterilización:**
  - se sortea qué material necesita autoclave (cinta testigo: rayas claras = sin esterilizar, oscuras = estéril);
  - el `Autoclave` tiene animación de meter (mano → boca → cámara) y de sacar (sale por la tapa y vuelve a la mano).
- **Solución salina:**
  - `LabMaterial.autoclavable = false` con `notAutoclavableReason`;
  - nunca sale sorteada para autoclave y el autoclave la rechaza;
  - **modo educativo:** al presionar E aparece la explicación;
  - **modo evaluación:** cuenta como 1 error.
- **Jeringa:** minijuego "Armar jeringa + aguja" en el **organizador de insumos** (`SupplyOrganizer` + `SyringeKitMinigame` + `SyringeCatalog`). Lo correcto es **5 mL + aguja 21G × 1″ (verde)**; al acertar, la jeringa aparece en la mano.
- **Final:** se coloca todo en la mesa de química (`PlacementZone`), calificación y guardado en SQLite.

### Nivel 2 · Preparación del animal (`Level2Manager`, raíz `Nivel2`)

- **Pasos:**
  1. Guantes (caja de guantes). **Al ponérselos, las manos del avatar se ven con guantes de nitrilo morados** (`M_Guante_Nitrilo`).
  2. Sacar al conejo de la jaula.
  3. Tarar la báscula.
  4. Pesar al conejo (2.5–4.0 kg): registrar si es apto o reportar si no lo es (se trae otro). Se guarda en SQLite y en el monitor de bitácora.
  5. Llevarlo al paño.
  6. Rasurar 5 × 5 cm (minijuego `DorsalMinigame`).
  7. Desinfectar con alcohol al 70 % (minijuego).
- **Conejo (`RabbitController`, prefab `Assets/objetos/Conejo/Conejo.prefab`):**
  - **Estrés:** sube si caminas rápido o giras bruscamente.
  - **Huida:** al llegar a 100 se escapa y huye saltando por el salón. Hay que atraparlo al aterrizar y estando cerca.
  - **Calmarlo:** mantener **clic derecho + mover el mouse de lado a lado** (acariciar).
- **Animación de acariciar:**
  - la mano derecha recorre el lomo de la cabeza a la cola siguiendo el mouse (`petStrokeStart/End`, `petStrokePixels`);
  - el conejo sube un poco en la vista (`petRaise`) y echa las orejas hacia atrás (`petEarFold` 25°);
  - en 1ª persona se ven los antebrazos y la mano (con guante); en 3ª persona y en el espejo lo hace el avatar;
  - mientras se acaricia, el aviso se reduce a 1 línea.
- **Final:** calificación = 50 % procedimiento + 30 % rasurado + 20 % desinfección. El botón "Continuar" lleva al nivel 3.

### Nivel 3 · Suspensión de glóbulos rojos / lavado de eritrocitos (`Level3Manager`, raíz `Nivel3`)

- **Ubicación:** todo sobre la mesa de química.
- **Objetos:**
  - **Centrífuga** (`Assets/objetos/Centrifugadora/Centrifugadora.fbx`, escalada ×2.6):
    - tapa con bisagra animada, rotor que gira, pantalla (rpm / tiempo, "TAPA ABIERTA", "FIN") y piloto;
    - zumbido procedural y pitidos;
    - rotor con 2 posiciones opuestas (muestra y balance).
  - **Gradilla** baja con 10 huecos; **tubo de muestra** (etiqueta amarilla) y **tubo de balance** (etiqueta azul, 9 mL de salina).
  - **Frasco de solución salina** (destapado), **vaso de desechos** y **micropipeta ajustada a 6 mL** con su **soporte**.
- **Ciclo de lavado (×3):**
  1. Cargar la micropipeta en el frasco y dispensar 6 mL en el tubo (que está en la gradilla).
  2. Resuspender: tubo en mano + clic derecho + mover el mouse; hay barra de progreso.
  3. Abrir la tapa (E) y colocar muestra y balance opuestos.
  4. Cerrar la tapa (R) y abrir el panel (R).
  5. Programar **exactamente 2500 rpm y 5 min**; cualquier otro valor bloquea y explica (en evaluación lo registra como error). Los 5 min se simulan en ~14 s.
  6. Abrir la tapa y sacar el tubo.
  7. Decantar en el vaso de desechos: se inclina de lado, cae un chorro y el paquete de eritrocitos se queda en el fondo.
- **Colores del sobrenadante:** 1º rojizo → 2º rosado → 3º transparente. Al sacar el tubo con el sobrenadante transparente, el nivel termina y se guarda `InsertResult(3, …)`.
- **Errores registrados:** tapa abierta, sin balance, sin resuspender, valores fuera del protocolo, decantar sin centrifugar, abrir con el rotor girando, salina sin decantar.
- **Scripts:** `Assets/Scripts/Level3/` (`SampleTube`, `Centrifuge`, `CentrifugePanel`, `Level3Station`, `MicropipetteTool`, `Level3Manager`).
- **Constructor:** **Tools ▸ Inmunologia ▸ Construir Nivel 3** (`Assets/Editor/Level3Builder.cs`). **Destruye y vuelve a crear** el objeto `Nivel3` y lo registra en `LevelDirector`, el menú y el pizarrón. Los ajustes hechos a mano en `Nivel3` se pierden si se vuelve a ejecutar.

## 6. Otros scripts de editor

`AvatarBuilder` (avatares, animaciones y emotes), `Level2Builder`, `Level3Builder`, `MenuBuilder`, `PlayerSetup`, `RabbitBuilder`, `SalonBuilder`, `FixMagentaMaterials`.

## 7. Pendientes y posibles mejoras

- **Baile de Homero (emote):** espera la imagen de referencia del usuario. La rueda ya tiene el espacio `Emote_BaileHomero` y muestra "próximamente" hasta que exista el estado en el Animator. Se agrega en `AvatarBuilder` (sección de emotes) con el menú Tools ▸ Inmunologia ▸ Emotes.
- **Mangas en 1ª persona:** se ven grandes y cuadradas al acariciar (el avatar es low-poly). Se ofreció mostrar solo la mano.
- **Mejoras opcionales que se ofrecieron:**
  - `GrabHitbox` en otros objetos delgados;
  - transparencia del avatar cuando la cámara de 3ª persona queda muy cerca;
  - guantes también en el nivel 3 (manejo de sangre).
- **Después de cada cambio:** recompilar el .exe y regenerar el zip.

---

**Primer paso sugerido:** conéctate a Unity, revisa `editor_status`, la jerarquía de la escena y la consola, y dime que estás listo. Después te diré qué sigue.
