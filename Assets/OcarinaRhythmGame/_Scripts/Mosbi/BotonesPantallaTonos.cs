using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Ejemplo independiente para una escena 2D:
/// cuatro botones de la interfaz reproducen cuatro tonos musicales.
/// No utiliza Arduino, MPR121 ni archivos de audio externos.
/// </summary>
public class BotonesPantallaTonos : MonoBehaviour
{
    [Header("Botones de la interfaz")]
    [Tooltip("Arrastra cuatro Button del Canvas en este orden: Do, Re, Mi y Sol.")]
    [SerializeField] private Button[] botones = new Button[4];

    [Header("Audio opcional")]
    [Tooltip("Opcional. Asigna aquí el AudioSource de la música de fondo para reducir su volumen.")]
    [SerializeField] private AudioSource musicaFondo;

    [Tooltip("Volumen de la música de fondo cuando se asigna en el campo anterior.")]
    [Range(0f, 1f)]
    [SerializeField] private float volumenMusicaFondo = 0.25f;

    [Tooltip("Volumen de las notas que reproducen los botones.")]
    [Range(0f, 1f)]
    [SerializeField] private float volumenNotas = 1f;

    [Header("Tonos generados")]
    [Tooltip("Duración de cada tono musical, en segundos.")]
    [Range(0.08f, 1f)]
    [SerializeField] private float duracionTono = 0.35f;

    // Frecuencia de muestreo estándar para crear los AudioClip.
    private const int FRECUENCIA_MUESTREO = 44100;

    // Frecuencias aproximadas de Do4, Re4, Mi4 y Sol4.
    private readonly float[] frecuencias =
    {
        261.63f,
        293.66f,
        329.63f,
        392.00f
    };

    private readonly string[] nombresNotas = { "Do", "Re", "Mi", "Sol" };

    // AudioSource separado: las notas no detienen ni reemplazan la música de fondo.
    private AudioSource reproductorNotas;

    // AudioClip creados en tiempo de ejecución, uno por botón.
    private AudioClip[] tonos;

    // Se guardan las acciones para poder quitarlas al desactivar este componente.
    private UnityAction[] accionesBoton;

    /// <summary>
    /// Awake prepara el reproductor, genera los cuatro tonos y crea
    /// las funciones que se conectarán a los botones.
    /// </summary>
    private void Awake()
    {
        CrearReproductorDeNotas();
        CrearTonos();

        accionesBoton = new UnityAction[4];

        for (int indice = 0; indice < accionesBoton.Length; indice++)
        {
            // Se copia el índice para que cada botón conserve su propia nota.
            int indiceLocal = indice;
            accionesBoton[indice] = () => TocarNota(indiceLocal);
        }

        // Si se asignó la música, la baja para que destaquen las notas.
        if (musicaFondo != null)
        {
            musicaFondo.volume = volumenMusicaFondo;
        }
    }

    /// <summary>
    /// OnEnable conecta los cuatro eventos de clic de la interfaz.
    /// </summary>
    private void OnEnable()
    {
        if (botones == null)
        {
            Debug.LogError("Asigna cuatro botones en el arreglo Botones.");
            return;
        }

        if (botones.Length < 4)
        {
            Debug.LogError("El arreglo Botones debe tener cuatro espacios.");
            return;
        }

        for (int indice = 0; indice < 4; indice++)
        {
            if (botones[indice] == null)
            {
                Debug.LogWarning("Falta asignar el botón número " + (indice + 1) + ".");
                continue;
            }

            // Quita primero la acción por seguridad y luego la agrega una vez.
            botones[indice].onClick.RemoveListener(accionesBoton[indice]);
            botones[indice].onClick.AddListener(accionesBoton[indice]);
        }
    }

    /// <summary>
    /// OnDisable desconecta las acciones para evitar llamadas duplicadas
    /// al desactivar o volver a activar el objeto.
    /// </summary>
    private void OnDisable()
    {
        if (botones == null || accionesBoton == null)
        {
            return;
        }

        int cantidad = Mathf.Min(botones.Length, accionesBoton.Length);

        for (int indice = 0; indice < cantidad; indice++)
        {
            if (botones[indice] != null && accionesBoton[indice] != null)
            {
                botones[indice].onClick.RemoveListener(accionesBoton[indice]);
            }
        }
    }

    /// <summary>
    /// Crea un AudioSource 2D independiente para las notas de la interfaz.
    /// </summary>
    private void CrearReproductorDeNotas()
    {
        GameObject objetoAudio = new GameObject("ReproductorNotasUI");
        objetoAudio.transform.SetParent(transform, false);

        reproductorNotas = objetoAudio.AddComponent<AudioSource>();
        reproductorNotas.playOnAwake = false;
        reproductorNotas.loop = false;
        reproductorNotas.spatialBlend = 0f;
        reproductorNotas.volume = 1f;
    }

    /// <summary>
    /// Genera en memoria un AudioClip distinto para cada uno de los cuatro botones.
    /// </summary>
    private void CrearTonos()
    {
        tonos = new AudioClip[4];

        int cantidadMuestras = Mathf.Max(
            1,
            Mathf.RoundToInt(FRECUENCIA_MUESTREO * duracionTono)
        );

        for (int indice = 0; indice < tonos.Length; indice++)
        {
            tonos[indice] = CrearTono(
                nombresNotas[indice],
                frecuencias[indice],
                cantidadMuestras
            );
        }
    }

    /// <summary>
    /// Construye un tono sinusoidal y aplica una entrada y salida gradual
    /// para que no produzca un clic brusco al comenzar o terminar.
    /// </summary>
    private AudioClip CrearTono(
        string nombreNota,
        float frecuencia,
        int cantidadMuestras)
    {
        AudioClip clip = AudioClip.Create(
            "Tono_" + nombreNota,
            cantidadMuestras,
            1,
            FRECUENCIA_MUESTREO,
            false
        );

        float[] muestras = new float[cantidadMuestras];

        for (int indice = 0; indice < cantidadMuestras; indice++)
        {
            float tiempo = (float)indice / FRECUENCIA_MUESTREO;

            // Ataque: el volumen sube en los primeros 10 milisegundos.
            float ataque = Mathf.Clamp01(tiempo / 0.01f);

            // Liberación: el volumen baja durante los últimos 50 milisegundos.
            float tiempoRestante = duracionTono - tiempo;
            float liberacion = Mathf.Clamp01(tiempoRestante / 0.05f);

            float envolvente = Mathf.Min(ataque, liberacion);
            float onda = Mathf.Sin(2f * Mathf.PI * frecuencia * tiempo);

            muestras[indice] = onda * 0.65f * envolvente;
        }

        clip.SetData(muestras, 0);
        return clip;
    }

    /// <summary>
    /// Reproduce el tono correspondiente al botón pulsado.
    /// PlayOneShot permite que las notas suenen sobre la música de fondo.
    /// </summary>
    private void TocarNota(int indiceNota)
    {
        if (indiceNota < 0 || indiceNota >= tonos.Length)
        {
            return;
        }

        reproductorNotas.PlayOneShot(tonos[indiceNota], volumenNotas);
        Debug.Log("Sonó la nota " + nombresNotas[indiceNota] + ".");
    }

    /// <summary>
    /// Libera los AudioClip generados cuando se destruye este objeto.
    /// </summary>
    private void OnDestroy()
    {
        if (tonos == null)
        {
            return;
        }

        for (int indice = 0; indice < tonos.Length; indice++)
        {
            if (tonos[indice] != null)
            {
                Destroy(tonos[indice]);
            }
        }
    }
}
