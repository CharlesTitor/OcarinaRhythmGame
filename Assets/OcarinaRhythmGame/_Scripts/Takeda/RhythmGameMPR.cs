using UnityEngine;
using System.Collections.Generic;

public class RhythmGameMPR : MonoBehaviour
{

    // Dibujar pistas, zonas, hacer caer y carriles y que diga Good o Bad

    [Header("Notes Movement")]
    [Tooltip("Velocidad al caer")]
    [Min(0.1f)]
    public float velocidadNota = 4f;

    [Tooltip("Time between spawns")]
    [Min(0.2f)]
    public float segundosEntresNotas = 1.2f;


    [Header("Correct Zone")]
    [Tooltip("Distancia")]
    [Min(0.05f)]
    public float margenExcelente = 0.35f;

    private SensorDetector puertoSerial;
    private Camera camaraPrincipal;
    private Sprite spriteCuadrada;
    private Texture2D texturaCuadrada;
    private readonly List<NotaEnEscena> notasActivas = new List<NotaEnEscena>();


    private float[] posicionesCarrilesX;
    private interface cantidadCarriles;
    private float posicionInicioNotasY;
    private float tiempoSiguienteNota;
    private int carrilNotaAnterior=-1;

    private readonly Color[] coloresCarril =
    {
        new Color(0.95f,0.15f,0.15f), //rojo
        new Color(0.15f,0.75f,0.25f), //verde
        new Color(0.95f,0.15f,0.15f), //amarillo
        new Color(0.15f,0.45f,0.95f) //azul
    };
    // paint 255,255,255 unity 1,1,1


    private class NotaEnEscena
    {
        public GameObject objeto;
        public int carril;
        public NotaEnEscena(GameObject objetoNota,int numeroCarril)
        {
            objeto = objetoNota;
            carril = numeroCarril;
        }
    }


    void Start()
    {
        puertoSerial = GetComponent<SensorDetector>();
        if (!puertoSerial)
        {
            Debug.Log("No se encuentra Sensor Detector en esto");
            enabled = false;
            return;
        }

        camaraPrincipal = Camera.main;
        if (!camaraPrincipal)
        {
            Debug.Log("No se encuentra la Camara");
            enabled = false;
            return;
        }

        cantidadCarriles = puertoSerial.cantidadCarriles;
        if (cantidadCarriles < 1 || cantidadCarriles > 12)
        {
            Debug.Log("La cantidad debe ser entre 1 a 12");
            enabled = false;
            return;
        }

        CrearSpriteCantidad();
        CrearPista();
        tiempoSiguienteNota = Time.time + 1.5f;
        Debug.Log(cantidadCarriles + " pistas creadas");
    }

    // Update is called once per frame
    void Update()
    {
        if (!puertoSerial || !puertoSerial.Conectado)
        {
            return;
        }
        MoverNota();
        if (Time.time >= tiempoSiguienteNota)
        {

        }
    }
}
