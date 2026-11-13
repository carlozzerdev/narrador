# Narrador

Aplicación de escritorio para Windows Forms y .NET 10 que convierte un archivo SRT en una narración sincronizada WAV o MP3 mediante ElevenLabs. La interfaz está en español y se divide en dos columnas ajustables: **Narrador** a la izquierda y **Vídeo** a la derecha. Los proveedores de síntesis están organizados en pestañas dentro de Narrador.

## Flujo de trabajo

La ventana tiene dos áreas separadas por un divisor que puedes arrastrar para cambiar sus anchos:

| Área | Para qué sirve |
| --- | --- |
| **Narrador**, a la izquierda | Cargar y corregir el SRT, configurar ElevenLabs y generar el archivo de audio. |
| **Vídeo**, a la derecha | Elegir un vídeo y su narración, crear el montaje, reproducirlo y compartirlo. |

El recorrido habitual es:

```text
SRT → revisión de bloques → voz y velocidad → audio WAV/MP3
                                                ↓
Vídeo original + narración → Aplicar narración → vídeo MP4/MKV → revisar y compartir
```

### 1. Cargar y revisar los subtítulos

En **Narrador**, pulsa **Seleccionar SRT…** y abre el guion. Puedes empezar con `samples/tutorial.srt`.

Edita **Inicio**, **Fin** y **Texto** directamente en la tabla. Los tiempos admiten `HH:mm:ss.fff` y `HH:mm:ss,fff`; el final debe ser posterior al inicio y los bloques deben estar ordenados y sin solapamientos. La duración se calcula automáticamente. Para eliminar uno o varios bloques, selecciona sus cabeceras de fila, usando Ctrl o Mayús para selección múltiple, y pulsa **Eliminar bloques seleccionados**. Los bloques restantes se renumeran y conservan sus tiempos.

Los cambios se mantienen en memoria y se comparten entre los servicios. El SRT original no se modifica ni se guarda una copia con las correcciones. Al cargar otro SRT o cerrar la aplicación se pierde esa edición.

### 2. Configurar ElevenLabs

En la pestaña **ElevenLabs**, introduce la API key y elige una **Voz guardada**: StoreTeller, Mari o Nico (PRO). El desplegable rellena el **ID de voz**, que también puedes editar para utilizar otra voz.

Ajusta estabilidad, similitud y **Velocidad (×)**. `1,00×` es velocidad normal; los valores inferiores ralentizan la narración y los superiores la aceleran. El rango disponible es `0,50×`–`2,00×`. **Guardar configuración** y **Cargar configuración** actúan sobre los ajustes de ElevenLabs, no sobre los bloques del SRT. Marca **Recordar en este usuario de Windows** si quieres conservar la clave cifrada.

### 3. Generar y escuchar la narración

Debajo de las pestañas, pulsa **Guardar como…** para elegir el nombre, la carpeta y el formato del audio, o utiliza el WAV sugerido junto al SRT. **WAV es el formato recomendado para montaje de vídeo**; MP3 también está disponible.

Pulsa **Generar audio**. Se validan los bloques antes de llamar al servicio y el progreso indica qué subtítulo se está procesando. Al terminar, usa **Reproducir audio** para escucharlo con la aplicación predeterminada de Windows o **Abrir carpeta** para localizarlo.

Después de generar aparecen **Inicio audio** y **Fin audio** con los tiempos adaptados del resultado. Las columnas editables **Inicio** y **Fin** conservan los tiempos de entrada: una nueva generación vuelve a partir de ellos y evita acumular desplazamientos. Si editas o eliminas bloques, los resultados de tiempos anteriores se ocultan hasta volver a generar.

La columna **Vídeo** recibe automáticamente la ruta del audio que acaba de generarse correctamente. No es necesario buscarlo de nuevo.

### 4. Elegir el vídeo y comprobar la narración

En **Vídeo**, pulsa **Seleccionar vídeo…**. El archivo se carga en el reproductor integrado como vista previa en pausa. En este momento estás viendo el **vídeo original**, con su sonido original si lo tiene.

Revisa el campo **Narración · automática o seleccionada**. Puedes utilizar el audio recibido de Narrador o pulsar **Seleccionar audio…** para elegir otro archivo. Cada nueva generación de audio en Narrador vuelve a rellenar este campo con el archivo recién generado, sustituyendo cualquier selección manual anterior.

Elegir una narración todavía no modifica la vista previa ni su sonido. Para obtener y escuchar el vídeo con esa narración, realiza el montaje del paso siguiente.

### 5. Crear el vídeo narrado

El campo **Vídeo de salida** propone un archivo junto al original, con el sufijo `-narrado`, por ejemplo `tutorial-narrado.mp4`. Usa **Guardar vídeo como…** si quieres cambiar el nombre, la carpeta o el formato MP4/MKV.

Pulsa **Aplicar narración al vídeo**. El botón se habilita cuando existen tanto el vídeo como el audio seleccionados. El montaje es local y no consume créditos de ElevenLabs. Durante el proceso puedes usar **Cancelar montaje**; mientras tanto, la generación de audio queda bloqueada.

Al terminar se crea el archivo de salida y el reproductor carga el **resultado narrado en pausa**. El vídeo y el audio de origen permanecen intactos. Si ya existe el destino, se pide confirmación antes de sustituirlo.

### 6. Revisar y compartir el resultado

Pulsa **Play** en el reproductor para revisar el montaje. Puedes pausar, cambiar de posición, ajustar el volumen o silenciarlo. **Reproducir vídeo** vuelve a cargar y reproduce el último montaje completado; **Abrir carpeta del vídeo** abre su ubicación.

Cuando quieras entregarlo, pulsa **Compartir**. Se abre el panel de Windows con el archivo que está cargado en el reproductor. Comprueba su nombre debajo de los controles: si has vuelto a seleccionar el original, compartirás ese original. Elige la aplicación de destino y completa el envío desde Windows.

### 7. Corregir y repetir

Si detectas un problema de texto, tiempos o velocidad, vuelve a **Narrador**, corrige los bloques o los ajustes y genera de nuevo. La nueva narración se seleccionará automáticamente a la derecha. Vuelve a pulsar **Aplicar narración al vídeo** para actualizar el montaje: regenerar el audio por sí solo no cambia un vídeo ya exportado.

## Trabajar con un vídeo y un audio existentes

Puedes utilizar solo la columna **Vídeo**, sin cargar un SRT ni introducir una API key:

1. Pulsa **Seleccionar vídeo…** y elige el original.
2. Pulsa **Seleccionar audio…** y elige una narración existente.
3. Revisa la salida y pulsa **Aplicar narración al vídeo**.
4. Reproduce el resultado y compártelo si lo necesitas.

El selector de audio admite WAV, MP3, M4A, AAC, FLAC, OGG y WMA. El audio se coloca desde el principio del vídeo: si debe respetar pausas o entradas concretas, esas pausas ya deben formar parte del propio archivo. El montaje no analiza ni consulta un SRT para sincronizar un audio externo.

## Qué hace el montaje de vídeo

### Imagen y sonido

El montaje conserva la primera pista de imagen **sin recomprimirla** y sustituye el sonido original por una única pista AAC con la narración seleccionada. No mezcla la voz con la música o el sonido anteriores. Tampoco copia otras pistas de vídeo, subtítulos ni adjuntos.

La duración del resultado se ajusta a la del vídeo original:

| Duración de la narración | Resultado |
| --- | --- |
| Más corta que el vídeo | La narración se conserva y se añade silencio hasta el final del vídeo. |
| Igual a la del vídeo | Se utiliza la narración completa. |
| Más larga que el vídeo | Se recorta la parte de narración que sobrepasa el final del vídeo. |

Por ejemplo, para un vídeo de 60 segundos, una narración de 45 segundos deja 15 segundos de silencio al final; una narración de 75 segundos pierde sus últimos 15 segundos. Si necesitas conservar una narración más larga, ajusta su texto o velocidad, o prepara antes un vídeo de mayor duración. El montaje no alarga la imagen.

Esto también se aplica al audio generado desde SRT: la adaptación de los bloques puede producir una narración más larga que el vídeo. Revisa la duración del audio y comprueba el final del montaje antes de compartirlo.

### Archivos de entrada y formatos de salida

El selector de vídeo muestra MP4, MOV, M4V, MKV, AVI, WebM y WMV, y permite buscar otros archivos. La lectura depende de que FFmpeg admita el formato y los códecs de la entrada.

| Entrada seleccionada | Salida sugerida |
| --- | --- |
| MP4, MOV o M4V | MP4 |
| Otros formatos | MKV |

Puedes cambiar el contenedor con **Guardar vídeo como…**. Si MP4 no admite el códec de imagen del original, prueba MKV. Cambiar entre MP4 y MKV no convierte ni recomprime esa imagen.

### Cancelación y conservación de archivos

La salida debe tener un nombre o una ubicación diferentes de los originales. El montaje se prepara en un archivo temporal junto al destino y solo se publica cuando termina correctamente. Un fallo o una cancelación mantiene intacto un destino que ya existiera; sustituirlo requiere confirmación. El estado muestra el tiempo de vídeo procesado y **Cancelar montaje** detiene el proceso local.

Para localizar el resultado, usa **Abrir carpeta del vídeo**. El archivo de narración se conserva por separado y puede reutilizarse en otros montajes.

## Reproductor y compartir

El reproductor está integrado en la columna **Vídeo**. Carga el original al seleccionarlo y carga el resultado al completar un montaje. Ambas cargas iniciales se hacen en pausa.

| Control | Función |
| --- | --- |
| **Play** | Inicia o reanuda la reproducción del archivo cargado. |
| **Pausa** | Detiene temporalmente la reproducción sin volver al inicio. |
| **Barra de posición** | Permite avanzar o retroceder a otra parte del vídeo. |
| **Tiempo** | Muestra la posición actual y la duración total. |
| **Volumen** | Ajusta el volumen de escucha del reproductor. |
| **Silenciar / Activar audio** | Desactiva o restaura el sonido de la vista previa. |
| **Compartir** | Abre el panel de Windows con el archivo cargado. |

El volumen, el silencio y la posición son controles de reproducción: no cambian el sonido ni la duración del archivo exportado. El nombre bajo los controles identifica el archivo que estás revisando y que se entregará al pulsar **Compartir**.

Compartir entrega el archivo de vídeo al panel nativo de Windows. Las aplicaciones de destino disponibles dependen de las que tengas instaladas y admitan compartir archivos. Abrir el panel no envía el vídeo automáticamente: elige el destino y termina la operación allí. También puedes usar **Abrir carpeta del vídeo** para acceder al resultado y adjuntarlo desde otra aplicación.

El reproductor utiliza LibVLCSharp.WinForms y LibVLC, incluidos en la carpeta de la aplicación. No requiere instalar VLC por separado. El montaje utiliza FFmpeg, también incluido en la publicación. Para mover o distribuir Narrador, copia **la carpeta completa**, incluidas `libvlc`, `tools` y `licenses`.

## Configuración local

La configuración pertenece al usuario de Windows y se guarda en `%LOCALAPPDATA%\Narrador\settings.json`, fuera del repositorio. La ruta habitual es `C:\Users\<usuario>\AppData\Local\Narrador\settings.json`. Ese archivo y la clave guardada no se incluyen en Git.

Los ajustes se recuerdan al guardar, al generar audio y al cerrar la aplicación. Las correcciones del SRT, las rutas seleccionadas de vídeo/narración y la posición del reproductor no se guardan como un proyecto recuperable.

**Guardar configuración** y **Cargar configuración** están dentro de la pestaña ElevenLabs y actúan sobre sus ajustes: catálogo de voces, ID activo, modelo, estabilidad, similitud, velocidad, carpeta de salida y preferencia de recordar la clave. Cargar vuelve a leer los valores guardados y descarta las modificaciones de configuración que todavía no se hayan guardado; los subtítulos del editor compartido se conservan.

Todo se almacena en `%LOCALAPPDATA%\Narrador\settings.json`. Al marcar **Recordar en este usuario de Windows**, la API key se guarda en `ProtectedApiKeys.elevenlabs`, cifrada con DPAPI para ese usuario. Al desmarcarlo y guardar se elimina la clave almacenada. La aplicación mantiene la lectura de `elevenlabs.key` de versiones anteriores y lo migra al JSON al guardar, eliminando el archivo antiguo solo después de completar la escritura. La clave nunca se guarda en texto claro ni se muestra en errores.

El catálogo `Voices` se conserva en ese mismo JSON, con estas voces iniciales:

| Nombre | ID |
| --- | --- |
| StoreTeller | `JBFqnCBsd6RMkjVDRZzb` |
| Mari | `uShnBlXHAqA27srS28g2` |
| Nico (PRO) | `FXn3GKRRt9FCEyTbrkaL` |

El icono de la aplicación muestra una cabeza hablando y está integrado en el ejecutable y en la ventana. Su fuente PNG, el ICO con siete tamaños y el prompt utilizado están en `src/Narrador.WinForms/Assets/`.

## Funcionalidades

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

## Compilar y ejecutar

Requisitos: Windows 10 (versión 2004 o posterior) / Windows 11 con componentes multimedia de Windows, SDK de .NET 10 y acceso a NuGet durante la primera restauración. Para preparar FFmpeg y FFprobe en una copia nueva del repositorio, ejecutar `./tools/Install-Ffmpeg.ps1` antes de compilar y de pasar las pruebas de vídeo (requiere Internet la primera vez). La compilación copia FFmpeg a `tools/ffmpeg` dentro de la carpeta ejecutable; no requiere instalación global. Para ejecutar la publicación dependiente del entorno se necesita **.NET Desktop Runtime 10**. Las ediciones Windows N requieren los componentes multimedia correspondientes.

Abrir `Narrador.sln` en una versión de Visual Studio compatible con .NET 10 y seleccionar `Narrador.WinForms` como proyecto de inicio. También se puede trabajar desde PowerShell:

```powershell
.\tools\Install-Ffmpeg.ps1
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

La columna de vídeo se implementa en `VideoCompositionPanel`. `ApplyNarrationToVideo` valida las rutas y utiliza el puerto `IVideoAudioComposer`, implementado por `FfmpegVideoAudioComposer`. `EmbeddedVideoPlayer` gestiona LibVLC y los controles de reproducción; `WindowsVideoFileSharer` abre Compartir mediante `DataTransferManagerInterop` y entrega el archivo como un `StorageFile`.

## Sincronización de los subtítulos

La narración utiliza el **SRT como línea de tiempo de referencia**, con las correcciones de tiempos realizadas en el editor compartido. Cada bloque empieza en su hora original o, si el audio anterior todavía no ha terminado, justo al finalizar el bloque anterior. Su final es el mayor entre el final original y el instante en que acaba su voz. Así, los silencios entre bloques y el tiempo libre dentro de un bloque permiten reducir o eliminar el retraso acumulado. Los bloques vuelven a sus tiempos originales en cuanto cabe toda la narración; nunca se adelantan respecto al SRT. La voz se conserva completa a la velocidad elegida. La duración total solo aumenta si todavía queda retraso al terminar el último bloque.

Por ejemplo, un bloque de `00:00:00,250` a `00:00:06,470` dispone de `6,220 s`. Si genera `7,119 s` de voz, su nuevo final es `00:00:07,369`. Si el siguiente bloque estaba previsto para `00:00:08,000`, mantiene ese inicio original porque el silencio disponible absorbe el exceso. Si estaba previsto para `00:00:07,000`, empieza en `00:00:07,369`; los bloques posteriores siguen intentando recuperar su horario original.

Los tiempos adaptados se mantienen en memoria y se muestran en las columnas de solo lectura **Inicio audio** y **Fin audio** después de generar, resaltando los bloques desplazados. Las columnas **Inicio** y **Fin** siguen mostrando los tiempos editables de entrada. Cada nueva generación parte de esos tiempos y del texto editado, evitando acumular desplazamientos. Al editar o eliminar bloques se ocultan los resultados anteriores. El archivo SRT de origen permanece intacto.

Los subtítulos originales deben estar ordenados y no solaparse. Esta validación se realiza antes de llamar al proveedor. La duración final llega hasta el final adaptado del último subtítulo; el SRT no especifica la duración total del vídeo después de ese punto.

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
