# Narrador

Aplicación de escritorio para Windows Forms y .NET 10 que convierte un archivo SRT en una narración sincronizada WAV o MP3 mediante ElevenLabs. La interfaz está en español y se divide en dos columnas ajustables: **Narrador** a la izquierda y **Vídeo** a la derecha. Los proveedores de síntesis están organizados en pestañas dentro de Narrador.

## Primera versión

- Selección y validación de archivos `.srt` con UTF-8, UTF-16 con BOM o Windows-1252.
- Editor compartido fuera de las pestañas de servicios, con número de bloque, inicio y fin editables, duración calculada y texto editable de cada subtítulo. Permite eliminar uno o varios bloques seleccionando sus filas. Conserva las tildes y la puntuación, une las líneas de cada intervención y elimina las etiquetas habituales de formato.
- Pestaña ElevenLabs con selector de voces guardadas, ID de voz editable, API key oculta, estabilidad, similitud y velocidad manual, y botones propios para guardar y cargar configuración.
- Archivo de salida, Generar audio, Cancelar, Reproducir audio y Abrir carpeta en un panel común debajo de las pestañas; utiliza el servicio seleccionado.
- Modelo inicial `eleven_multilingual_v2`, voz inicial `JBFqnCBsd6RMkjVDRZzb`, estabilidad `0.50` y similitud `0.75`.
- Salida WAV PCM mono de 16 bits a 44.1 kHz para montaje de vídeo, o MP3 a 44.1 kHz y 128 kbps, con nombre y ubicación seleccionables.
- Una petición por subtítulo, con un máximo de 10 000 caracteres y contexto de las intervenciones anterior y siguiente.
- Silencio inicial, entre intervenciones y hasta el final del último subtítulo según los tiempos absolutos del SRT.
- Adaptación automática en memoria: las frases largas retrasan solo los bloques necesarios y los silencios disponibles absorben ese retraso para recuperar los tiempos originales, conservando la velocidad elegida.
- Generación asíncrona con progreso, cancelación, reproducción en la aplicación predeterminada de Windows y apertura de la carpeta de salida.
- Columna **Vídeo** con selección del vídeo, narración automática o elegida manualmente y salida MP4/MKV, conservando la imagen sin recomprimirla.
- Reproductor de vídeo integrado con Play, Pausa, volumen, silencio, barra de posición y Compartir mediante el panel de Windows.
- Configuración local y almacenamiento opcional de la clave con DPAPI para el usuario de Windows actual.

La narración utiliza el **SRT como línea de tiempo de referencia**, con las correcciones de tiempos realizadas en el editor compartido. Cada bloque empieza en su hora original o, si el audio anterior todavía no ha terminado, justo al finalizar el bloque anterior. Su final es el mayor entre el final original y el instante en que acaba su voz. Así, los silencios entre bloques y el tiempo libre dentro de un bloque permiten reducir o eliminar el retraso acumulado. Los bloques vuelven a sus tiempos originales en cuanto cabe toda la narración; nunca se adelantan respecto al SRT. La voz se conserva completa a la velocidad elegida. La duración total solo aumenta si todavía queda retraso al terminar el último bloque.

Por ejemplo, un bloque de `00:00:00,250` a `00:00:06,470` dispone de `6,220 s`. Si genera `7,119 s` de voz, su nuevo final es `00:00:07,369`. Si el siguiente bloque estaba previsto para `00:00:08,000`, mantiene ese inicio original porque el silencio disponible absorbe el exceso. Si estaba previsto para `00:00:07,000`, empieza en `00:00:07,369`; los bloques posteriores siguen intentando recuperar su horario original.

Los tiempos adaptados se mantienen en memoria y se muestran en las columnas de solo lectura **Inicio audio** y **Fin audio** después de generar, resaltando los bloques desplazados. Las columnas **Inicio** y **Fin** siguen mostrando los tiempos editables de entrada. Cada nueva generación parte de esos tiempos y del texto editado, evitando acumular desplazamientos. Al editar o eliminar bloques se ocultan los resultados anteriores. El archivo SRT de origen permanece intacto.

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

`MainForm` contiene el editor compartido `SubtitleEditor` y, debajo, las pestañas de servicios. Un `SplitContainer` separa Narrador de Vídeo y permite ajustar el ancho de ambas columnas. `SubtitleEditor` carga el SRT mediante `LoadSrt`, permite editar y eliminar bloques y valida los tiempos y el texto. `ElevenLabsTab` contiene únicamente los ajustes de ElevenLabs e implementa `IServiceConfiguration`. `AudioGenerationPanel`, debajo de las pestañas, gestiona la salida, la generación, la cancelación y la apertura del resultado, y llama a `GenerateSynchronizedNarration` con la configuración del servicio seleccionado y los bloques actuales del editor compartido. Durante la generación se bloquean tanto las pestañas como el editor. `ITimedAudioExporter` y `ITimedAudioExportSession` son los puertos de exportación; `SrtAudioExporter` los implementa con NAudio. El código de distribución de controles está separado en `ElevenLabsTab.Designer.cs`. El caso de uso anterior `GenerateNarration` permanece disponible para generar narración continua desde código; la interfaz utiliza la generación sincronizada.

Para incorporar otro proveedor:

1. Implementar `ISpeechSynthesizer` en infraestructura con su identificador y límite de caracteres.
2. Registrar la instancia en `Program.cs`, en la colección del caso de uso `GenerateSynchronizedNarration`.
3. Añadir su control de configuración y pestaña a `MainForm`, implementando `IServiceConfiguration`. Los controles comunes utilizarán automáticamente el servicio de la pestaña seleccionada.
4. Reutilizar la lectura del SRT, validación temporal y exportación sincronizada del audio.

## Compilar y ejecutar

Requisitos: Windows 10 (versión 2004 o posterior) / Windows 11 con componentes multimedia de Windows, SDK de .NET 10 y acceso a NuGet durante la primera restauración. Para preparar FFmpeg y FFprobe en una copia nueva del repositorio, ejecutar `./tools/Install-Ffmpeg.ps1` antes de compilar y de pasar las pruebas de vídeo (requiere Internet la primera vez). La compilación copia FFmpeg a `tools/ffmpeg` dentro de la carpeta ejecutable; no requiere instalación global. Para ejecutar la publicación dependiente del entorno se necesita **.NET Desktop Runtime 10**. Las ediciones Windows N requieren los componentes multimedia correspondientes.

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
2. En el editor situado encima de las pestañas, editar **Inicio**, **Fin** y **Texto**. Los tiempos usan `HH:mm:ss.fff` o `HH:mm:ss,fff`; el fin debe ser posterior al inicio y los bloques deben permanecer ordenados y sin solapamientos. Para eliminar bloques, seleccionar sus cabeceras de fila (Ctrl/Mayús para varios) y pulsar **Eliminar bloques seleccionados**. Los bloques restantes se renumeran sin mover sus tiempos. Después de generar aparecen **Inicio audio** y **Fin audio** con los tiempos adaptados. Los cambios son comunes a los servicios y se mantienen en memoria; el archivo SRT original no se modifica.
3. En ElevenLabs, introducir una API key con acceso a síntesis y elegir **StoreTeller**, **Mari** o **Nico (PRO)** en **Voz guardada**, o escribir otro **ID de voz**. Seleccionar una voz rellena el ID; escribir un ID diferente lo conserva como voz personalizada.
4. Ajustar estabilidad, similitud y **Velocidad (×)** si se desea. La velocidad admite valores entre `0,50` y `2,00`, con incrementos de `0,01`: `1,00` es normal, valores superiores aceleran la voz y valores inferiores la ralentizan. Este ajuste se aplica a todos los subtítulos de la generación y se recuerda al guardar la configuración o cerrar la aplicación. Dentro de `0,70`–`1,20` se utiliza `voice_settings.speed` de ElevenLabs. Fuera de ese rango se solicita el límite nativo más cercano y se aplica localmente el factor restante conservando el tono. La duración resultante se mide y los tiempos se adaptan en memoria cuando hace falta ampliar un bloque.
5. Elegir WAV o MP3 y la ruta con **Guardar como…**, o utilizar el WAV sugerido junto al SRT.
6. Pulsar **Generar audio**. Si el destino existe, confirmar su sustitución.
7. Escuchar el resultado con **Reproducir audio**.
8. En la columna **Vídeo**, a la derecha, pulsar **Seleccionar vídeo…**. El reproductor integrado carga una vista previa en pausa. El campo **Narración** se completa automáticamente cada vez que se genera audio; también permite elegir un WAV, MP3 u otro audio con **Seleccionar audio…**, sin necesidad de sintetizarlo en la aplicación. La salida se sugiere junto al vídeo con el sufijo `-narrado`; puede cambiarse con **Guardar vídeo como…**.
9. Pulsar **Aplicar narración al vídeo**. Permite cancelar el montaje, reproducir el resultado dentro de la aplicación o abrir su carpeta. Al completar el montaje, el reproductor carga el resultado en pausa.
10. Usar **Play**, **Pausa**, **Silenciar**, el volumen y la barra de posición del reproductor. **Compartir** abre el panel nativo de Windows con el archivo que está cargado en el reproductor; el usuario elige la aplicación de destino y completa el envío.

La naturalidad depende también de la voz, el guion y los ajustes. Conviene evaluar una muestra de texto representativa antes de procesar un tutorial largo.

**Guardar configuración** y **Cargar configuración** están dentro de la pestaña ElevenLabs y actúan sobre sus ajustes: catálogo de voces, ID activo, modelo, estabilidad, similitud, velocidad, carpeta de salida y preferencia de recordar la clave. Cargar vuelve a leer los valores guardados y descarta las modificaciones de configuración que todavía no se hayan guardado; los subtítulos del editor compartido se conservan.

Todo se almacena en `%LOCALAPPDATA%\Narrador\settings.json`. Al marcar **Recordar en este usuario de Windows**, la API key se guarda en `ProtectedApiKeys.elevenlabs`, cifrada con DPAPI para ese usuario. Al desmarcarlo y guardar se elimina la clave almacenada. La aplicación mantiene la lectura de `elevenlabs.key` de versiones anteriores y lo migra al JSON al guardar, eliminando el archivo antiguo solo después de completar la escritura. La clave nunca se guarda en texto claro ni se muestra en errores.

El catálogo `Voices` se conserva en ese mismo JSON, con estas voces iniciales:

| Nombre | ID |
| --- | --- |
| StoreTeller | `JBFqnCBsd6RMkjVDRZzb` |
| Mari | `uShnBlXHAqA27srS28g2` |
| Nico (PRO) | `FXn3GKRRt9FCEyTbrkaL` |

El icono de la aplicación muestra una cabeza hablando y está integrado en el ejecutable y en la ventana. Su fuente PNG, el ICO con siete tamaños y el prompt utilizado están en `src/Narrador.WinForms/Assets/`.

## Aplicar la narración al vídeo

El montaje es local, utiliza FFmpeg y no consume créditos de ElevenLabs. `ApplyNarrationToVideo` valida las rutas y `IVideoAudioComposer` conecta el caso de uso con el adaptador `FfmpegVideoAudioComposer`. La interfaz es común a los servicios y utiliza el audio completado en el paso anterior o el archivo seleccionado manualmente; mientras se monta el vídeo no se puede regenerar ese audio. Una nueva generación sustituye automáticamente la selección manual por el audio recién generado.

Se conserva la primera pista de vídeo sin recomprimirla y se sustituye el sonido original por una única pista AAC de la narración. La duración se ajusta a la del vídeo: si la narración termina antes, se añade silencio; si termina después, se recorta al final del vídeo. Los silencios y tiempos de sincronización ya presentes en el audio generado se conservan.

La salida puede ser MP4 o MKV. Para entradas MP4/MOV/M4V se sugiere MP4; para los demás formatos se sugiere MKV, que admite más códecs sin recomprimir la imagen. Si un códec de entrada no cabe en MP4, guardar como MKV. Solo se incluyen la primera pista de imagen y la narración; no se copian pistas de sonido anteriores, subtítulos ni adjuntos.

Los originales no se sobrescriben. El montaje se escribe en un archivo temporal junto al destino y solo se publica al terminar correctamente. Si se cancela o falla, un destino anterior permanece intacto. Sustituir un resultado existente requiere confirmación. El progreso muestra el tiempo procesado y la cancelación detiene el proceso de FFmpeg.

FFmpeg se busca primero en `tools/ffmpeg/ffmpeg.exe` junto a la aplicación, después junto al ejecutable y finalmente en PATH. La publicación incluye el binario, su licencia y la identificación de la compilación. Para distribuir la aplicación, copiar la carpeta completa.
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

## Reproductor y compartir

El reproductor utiliza LibVLCSharp.WinForms y LibVLC, distribuidos dentro de la carpeta de la aplicación, sin requerir una instalación independiente de VLC. Permite revisar el vídeo original o el montaje final. La vista previa se carga pausada para evitar que se reproduzca sonido sin pulsar Play. Antes de un montaje se libera el archivo abierto para permitir sustituir un resultado anterior.

Compartir usa `DataTransferManagerInterop` con el identificador de ventana de Windows Forms y entrega el vídeo como un `StorageFile`, no solo como una ruta de texto. El programa abre el panel de Windows; el usuario decide el destino y realiza el envío. Las aplicaciones ofrecidas dependen de las que tenga instaladas y admitan compartir archivos.

## Verificación

Las pruebas cubren lectura del SRT, codificación de texto, continuidad entre intervenciones, parámetros válidos de voz, contrato HTTP con un servidor simulado, errores del proveedor, dirección de dependencias, almacenamiento DPAPI y exportación real de WAV/MP3 con audio sintético. Se verifican los inicios, los silencios, los intervalos contiguos, la conservación del audio completo y del archivo SRT, la ampliación del último bloque, la recuperación total o progresiva de retrasos mediante silencios, la recuperación dentro de un bloque con voz corta, la ausencia de solapamientos cuando no hay margen y la preservación del destino al cancelar. También se verifica el reparto entre velocidad nativa y factor local, el cambio de duración y la conservación de la frecuencia de un tono de prueba durante el procesamiento de tempo.

La prueba de Windows Forms verifica que el editor está fuera de las pestañas, carga el SRT de ejemplo, edita texto y tiempos, rechaza formatos, intervalos y solapamientos inválidos y elimina bloques. Comprueba que la generación recibe los bloques editados y omite los eliminados, que el SRT original permanece intacto y que los tiempos adaptados se muestran aparte sin alterar la entrada de una nueva generación. También comprueba el bloqueo del editor durante la generación, la eliminación de todos los bloques, el catálogo de voces, la edición manual de IDs, Guardar/Cargar configuración y el envío a otro servicio seleccionado mediante los controles comunes. Verifica los tamaños inicial y mínimo y que los botones permanezcan dentro de sus contenedores. Las pruebas de persistencia cubren el catálogo, la clave cifrada en el mismo JSON y la migración del archivo de clave anterior. Guarda imágenes en `artifacts/qa/` para revisión visual.

Las pruebas de vídeo crean material sintético con FFmpeg y verifican la sustitución real del sonido, el silencio final, el recorte de narraciones largas, la duración del resultado, la igualdad de la pista de imagen mediante hash, la entrada sin sonido y la conservación de originales y destinos al cancelar o fallar. La prueba de interfaz comprueba la distribución en dos columnas, la transferencia automática de la narración, la sustitución manual del audio y el retorno al audio recién generado en una nueva síntesis. Guarda una captura adicional en `artifacts/qa/winforms-video.png`. Una prueba con un vídeo sintético reproducido realmente en LibVLC verifica Play, Pausa, posición, volumen, silencio y la entrega del archivo cargado a la acción Compartir sin realizar ningún envío. También comprueba que se liberan los archivos al cerrar el reproductor.

Las pruebas no llaman al servicio real de ElevenLabs ni consumen créditos. La evaluación del acento, la naturalidad y la voz requiere escuchar una generación real con la cuenta del usuario.

## Referencias

- [API de síntesis de ElevenLabs](https://elevenlabs.io/docs/api-reference/text-to-speech/convert)
- [Modelos y límites de ElevenLabs](https://elevenlabs.io/docs/overview/models)
- [Pausas y buenas prácticas de ElevenLabs](https://elevenlabs.io/docs/overview/capabilities/text-to-speech/best-practices)
- [FFmpeg: selección de pistas y montaje](https://ffmpeg.org/ffmpeg.html)
- [FFmpeg: filtro apad](https://ffmpeg.org/ffmpeg-filters.html#apad)
- [LibVLCSharp: integración](https://docs.videolan.me/libvlcsharp/docs/getting_started.html)
- [Compartir archivos desde aplicaciones de escritorio Windows](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/integrate-sharesheet-send)
- [NAudio](https://github.com/naudio/NAudio)
- [SoundTouch.Net](https://github.com/owoudenberg/soundtouch.net)
