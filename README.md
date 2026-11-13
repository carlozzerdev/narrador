# Narrador

Aplicación de escritorio para Windows Forms y .NET 10 que convierte un archivo SRT en una narración sincronizada WAV o MP3 mediante ElevenLabs. La interfaz está en español y está organizada por proveedores en pestañas.

## Primera versión

- Selección y validación de archivos `.srt` con UTF-8, UTF-16 con BOM o Windows-1252.
- Tabla con índice, inicio, fin, duración disponible y texto editable de cada subtítulo. Conserva las tildes y la puntuación, une las líneas de cada intervención y elimina las etiquetas habituales de formato.
- Pestaña ElevenLabs con ID de voz, API key oculta, estabilidad, similitud y velocidad manual.
- Modelo inicial `eleven_multilingual_v2`, voz inicial `JBFqnCBsd6RMkjVDRZzb`, estabilidad `0.50` y similitud `0.75`.
- Salida WAV PCM mono de 16 bits a 44.1 kHz para montaje de vídeo, o MP3 a 44.1 kHz y 128 kbps, con nombre y ubicación seleccionables.
- Una petición por subtítulo, con un máximo de 10 000 caracteres y contexto de las intervenciones anterior y siguiente.
- Silencio inicial, entre intervenciones y hasta el final del último subtítulo según los tiempos absolutos del SRT.
- Adaptación automática en memoria: las frases largas retrasan solo los bloques necesarios y los silencios disponibles absorben ese retraso para recuperar los tiempos originales, conservando la velocidad elegida.
- Generación asíncrona con progreso, cancelación, reproducción en la aplicación predeterminada de Windows y apertura de la carpeta de salida.
- Configuración local y almacenamiento opcional de la clave con DPAPI para el usuario de Windows actual.

La narración utiliza el **SRT como línea de tiempo de referencia**. Cada bloque empieza en su hora original o, si el audio anterior todavía no ha terminado, justo al finalizar el bloque anterior. Su final es el mayor entre el final original y el instante en que acaba su voz. Así, los silencios entre bloques y el tiempo libre dentro de un bloque permiten reducir o eliminar el retraso acumulado. Los bloques vuelven a sus tiempos originales en cuanto cabe toda la narración; nunca se adelantan respecto al SRT. La voz se conserva completa a la velocidad elegida. La duración total solo aumenta si todavía queda retraso al terminar el último bloque.

Por ejemplo, un bloque de `00:00:00,250` a `00:00:06,470` dispone de `6,220 s`. Si genera `7,119 s` de voz, su nuevo final es `00:00:07,369`. Si el siguiente bloque estaba previsto para `00:00:08,000`, mantiene ese inicio original porque el silencio disponible absorbe el exceso. Si estaba previsto para `00:00:07,000`, empieza en `00:00:07,369`; los bloques posteriores siguen intentando recuperar su horario original.

Los tiempos adaptados se mantienen en memoria y se muestran resaltados en la tabla después de generar. El archivo SRT de origen permanece intacto. Cada nueva generación vuelve a calcular el ajuste desde los tiempos originales del archivo, conservando las correcciones de texto hechas en la tabla y evitando sumar de nuevo el desplazamiento de una ejecución anterior. Las celdas de tiempo muestran los valores originales en su información emergente.

Los subtítulos originales deben estar ordenados y no solaparse. Esta validación se realiza antes de llamar al proveedor. La duración final llega hasta el final adaptado del último subtítulo; el SRT no especifica la duración total del vídeo después de ese punto.

## Arquitectura Clean

```text
src/
  Narrador.Domain/           Subtítulos y parámetros válidos de voz.
  Narrador.Application/      Casos de uso, preparación del texto y puertos.
  Narrador.Infrastructure/   HTTP ElevenLabs, línea de tiempo WAV/MP3 y protección de claves.
  Narrador.WinForms/         Ventana principal, pestañas y composición de dependencias.
tests/
  Narrador.Tests/            Pruebas de dominio, aplicación, adaptadores e interfaz.
samples/
  tutorial.srt               Guion de ejemplo.
```

Dependencias entre proyectos:

```text
Domain         → ninguna otra capa
Application    → Domain
Infrastructure → Application
WinForms       → Application + Infrastructure (composición en Program.cs)
```

`Domain` y `Application` utilizan `net10.0` y no dependen de Windows Forms, HTTP ni NAudio. Los adaptadores de infraestructura implementan las interfaces de aplicación. La composición es explícita mediante inyección por constructor en `Program.cs`; no se utiliza un localizador de servicios.

`MainForm` contiene las pestañas. `ElevenLabsTab` gestiona la presentación y llama a los casos de uso `LoadSrt` y `GenerateSynchronizedNarration`. `ITimedAudioExporter` y `ITimedAudioExportSession` son los puertos de exportación; `SrtAudioExporter` los implementa con NAudio. El código de distribución de controles está separado en `ElevenLabsTab.Designer.cs`. El caso de uso anterior `GenerateNarration` permanece disponible para generar narración continua desde código; la interfaz utiliza la generación sincronizada.

Para incorporar otro proveedor:

1. Implementar `ISpeechSynthesizer` en infraestructura con su identificador y límite de caracteres.
2. Registrar la instancia en `Program.cs`, en la colección del caso de uso `GenerateSynchronizedNarration`.
3. Añadir su control de presentación y pestaña a `MainForm`.
4. Reutilizar la lectura del SRT, validación temporal y exportación sincronizada del audio.

## Compilar y ejecutar

Requisitos: Windows 10/11 con componentes multimedia de Windows, SDK de .NET 10 y acceso a NuGet durante la primera restauración. Para ejecutar la publicación dependiente del entorno se necesita **.NET Desktop Runtime 10**. Las ediciones Windows N requieren los componentes multimedia correspondientes.

Abrir `Narrador.sln` en una versión de Visual Studio compatible con .NET 10 y seleccionar `Narrador.WinForms` como proyecto de inicio. También se puede trabajar desde PowerShell:

```powershell
dotnet restore Narrador.sln
dotnet build Narrador.sln -c Release --no-restore
dotnet test Narrador.sln -c Release --no-restore
dotnet run --project src/Narrador.WinForms -c Release --no-build
```

Publicar una carpeta ejecutable:

```powershell
dotnet publish src/Narrador.WinForms/Narrador.WinForms.csproj -c Release --no-restore -o artifacts/app
```

Ejecutar `artifacts/app/Narrador.WinForms.exe`. Para distribuirla hay que copiar **la carpeta completa**.

## Uso

1. Pulsar **Seleccionar SRT** y abrir el guion. Hay un ejemplo en `samples/tutorial.srt`.
2. Revisar o editar el texto de cada subtítulo en la tabla. Al cargar se muestran los tiempos del archivo; después de generar se muestran los tiempos adaptados en memoria.
3. Introducir una API key de ElevenLabs con acceso a síntesis de voz y revisar el ID de voz.
4. Ajustar estabilidad, similitud y **Velocidad (×)** si se desea. La velocidad admite valores entre `0,50` y `2,00`, con incrementos de `0,01`: `1,00` es normal, valores superiores aceleran la voz y valores inferiores la ralentizan. Este ajuste se aplica a todos los subtítulos de la generación y se recuerda al guardar la configuración o cerrar la aplicación. Dentro de `0,70`–`1,20` se utiliza `voice_settings.speed` de ElevenLabs. Fuera de ese rango se solicita el límite nativo más cercano y se aplica localmente el factor restante conservando el tono. La duración resultante se mide y los tiempos se adaptan en memoria cuando hace falta ampliar un bloque.
5. Elegir WAV o MP3 y la ruta con **Guardar como…**, o utilizar el WAV sugerido junto al SRT.
6. Pulsar **Generar audio**. Si el destino existe, confirmar su sustitución.
7. Escuchar el resultado con **Reproducir audio**.

La naturalidad depende también de la voz, el guion y los ajustes. Conviene evaluar una muestra de texto representativa antes de procesar un tutorial largo.

**Guardar configuración** recuerda los parámetros y la carpeta de salida. Al marcar **Recordar en este usuario de Windows**, se guarda además la clave cifrada. Al desmarcarlo y guardar, se elimina la clave almacenada. Los datos se escriben en `%LOCALAPPDATA%\Narrador`: `settings.json` contiene preferencias y `elevenlabs.key` contiene exclusivamente el secreto protegido. La clave no se incluye en el código, en la configuración JSON ni en mensajes de error.

## Tratamiento del audio y errores

La API devuelve un MP3 por subtítulo. Las respuestas de error se traducen a mensajes en español. Cada clip se decodifica a PCM mono de 16 bits a 44.1 kHz y se mide antes de solicitar el siguiente. Si supera su intervalo, el caso de uso amplía la duración de ese bloque y adapta los siguientes en memoria. El retraso se absorbe usando los silencios disponibles, conservando los inicios y finales originales siempre que sea posible. La generación continúa y el exportador coloca los clips completos en los tiempos adaptados.

Los clips se colocan en una línea de tiempo calculada con tiempos absolutos, redondeados a la muestra más cercana. Esto evita acumular desfase al añadir pausas. El audio utiliza la velocidad elegida y se inserta silencio digital donde no hay voz; las pausas internas que haya generado la voz también forman parte de su duración.

### Rango de velocidad ampliado

ElevenLabs documenta un rango nativo de `0,70`–`1,20`, con valor inicial `1,00`. La aplicación admite `0,50`–`2,00` mediante una combinación de síntesis nativa y cambio de tempo local con SoundTouch.Net:

| Velocidad elegida | Velocidad enviada a ElevenLabs | Factor local |
| --- | --- | --- |
| 0,50× | 0,70× | 0,50 / 0,70 ≈ 0,714× |
| 1,00× | 1,00× | 1,00× |
| 1,15× | 1,15× | 1,00× |
| 2,00× | 1,20× | 2,00 / 1,20 ≈ 1,667× |

El procesamiento local actúa sobre las muestras PCM de cada intervención antes de medir su duración y antes de insertar los silencios. Conserva el tono en lugar de modificar la frecuencia de reproducción. A velocidades extremas puede cambiar la naturalidad o aparecer algún artefacto; conviene escuchar el resultado. La adaptación de tiempos utiliza la duración final del audio ya procesado, sin acelerar automáticamente la voz para encajarla.

SoundTouch.Net se distribuye como DLL independiente sin modificaciones. Su licencia y la referencia al código fuente de la versión utilizada se incluyen en `THIRD-PARTY-NOTICES.txt` y `licenses/SoundTouch.Net-LGPL-2.1.txt`, también en la carpeta publicada.

**WAV es la salida recomendada para montaje de vídeo**: conserva exactamente las posiciones de la línea de tiempo PCM, con una resolución de una muestra (aproximadamente 0.023 ms a 44.1 kHz). Guardar como WAV no recupera la información perdida en el MP3 que devuelve el proveedor, pero evita una nueva compresión con pérdida. MP3 también contiene la línea de tiempo sincronizada; su codificación puede añadir retardo o relleno de unos milisegundos según el reproductor. Para una duración y colocación de muestras verificables utiliza WAV.

Las etiquetas `<break time="1.5s" />` que admite Multilingual v2 permiten solicitar pausas al modelo, pero no fijan la duración de lo que se pronuncia antes ni sincronizan una línea de tiempo SRT. Esta aplicación genera los silencios localmente conforme a la línea de tiempo adaptada.

El trabajo se prepara en una carpeta temporal junto al destino y se publica al completar la operación. Una cancelación o un fallo anterior a la publicación mantiene intacto un archivo de destino existente. Los temporales se eliminan al finalizar siempre que Windows permita hacerlo. Un cierre forzado del proceso puede dejar una carpeta `.narrador-*` junto al destino.

No se reintentan automáticamente las peticiones de síntesis. Cancelar o recibir un error no garantiza que ElevenLabs no haya consumido cuota por los fragmentos que ya haya procesado. Los fragmentos de una generación fallida no se conservan para reanudarla.

## Verificación

Las pruebas cubren lectura del SRT, codificación de texto, continuidad entre intervenciones, parámetros válidos de voz, contrato HTTP con un servidor simulado, errores del proveedor, dirección de dependencias, almacenamiento DPAPI y exportación real de WAV/MP3 con audio sintético. Se verifican los inicios, los silencios, los intervalos contiguos, la conservación del audio completo y del archivo SRT, la ampliación del último bloque, la recuperación total o progresiva de retrasos mediante silencios, la recuperación dentro de un bloque con voz corta, la ausencia de solapamientos cuando no hay margen y la preservación del destino al cancelar. También se verifica el reparto entre velocidad nativa y factor local, el cambio de duración y la conservación de la frecuencia de un tono de prueba durante el procesamiento de tempo.

La prueba de Windows Forms carga el SRT de ejemplo, comprueba los textos editables y los tiempos de solo lectura y verifica que los tiempos adaptados se muestran en la tabla, conservando los originales para una nueva generación. También comprueba los tamaños inicial y mínimo y que los botones permanezcan dentro de sus contenedores. Guarda imágenes en `artifacts/qa/` para revisión visual.

Las pruebas no llaman al servicio real de ElevenLabs ni consumen créditos. La evaluación del acento, la naturalidad y la voz requiere escuchar una generación real con la cuenta del usuario.

## Referencias

- [API de síntesis de ElevenLabs](https://elevenlabs.io/docs/api-reference/text-to-speech/convert)
- [Modelos y límites de ElevenLabs](https://elevenlabs.io/docs/overview/models)
- [Pausas y buenas prácticas de ElevenLabs](https://elevenlabs.io/docs/overview/capabilities/text-to-speech/best-practices)
- [NAudio](https://github.com/naudio/NAudio)
- [SoundTouch.Net](https://github.com/owoudenberg/soundtouch.net)
