using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

/* Recibir mensajes del Arduino
 * T,0
 * R,6
 * */

public class SensorDetector : MonoBehaviour
{

    [Header("Conexion con Arduino")]
    [Tooltip("Puerto COM de Arduino")]
    public string nombrePuerto = "COM8";
    [Tooltip("Velocidad de Baudios")]
    public int velocidad = 115200;
    [Header("Botones del Juego")]
    [Tooltip("Cantidad de botones")]
    [Range(1, 12)]
    public int cantidadCarriles = 6;

    private SerialPort puerto;
    private Thread hiloLectura;
    //"volatile" deja que ambos componentes vean el variable
    private volatile bool leyendo = false;
    private readonly ConcurrentQueue<string> colaMensaje = new ConcurrentQueue<string>();

    //Estados de Botones
    private readonly bool[] presionadoEstaFrame = new bool[12];
    private readonly bool[] siguePresionado = new bool[12];
    public bool Conectado { get; private set; }


    void Awake()
    {

    }

    void Start()
    {
        AbrirPuerto();
    }


    void Update()
    {
        Array.Clear(presionadoEstaFrame, 0, presionadoEstaFrame.Length);
        while (colaMensaje.TryDequeue(out string mensaje))
        {
            ProcesarMensaje(mensaje);
        }
    }


    private void AbrirPuerto()
    {
        try
        {
            puerto = new SerialPort(nombrePuerto, velocidad);
            puerto.NewLine = "\n";
            puerto.ReadTimeout = 100;
            puerto.Open();
            Conectado = true;
            leyendo = true;
            //hiloLectura = new Thread(LeerPuertoSegundoPlano());
            hiloLectura.IsBackground = true;
            hiloLectura.Start();
            Debug.Log("Puerto abierto " + nombrePuerto + " a " + velocidad + " baudios.");
        }
        catch (Exception e)
        {
            Conectado = false;
            leyendo = false;
            Debug.Log("No se pudo abrir " + nombrePuerto + "Detalle: " + e.Message);
        }
    }

    private void LeerPuertoSegundoPlano()
    {
        while (leyendo)
        {
            try
            {
                string mensaje = puerto.ReadLine();
                mensaje = mensaje.Trim();
                if (!string.IsNullOrEmpty(mensaje))
                {
                    colaMensaje.Enqueue(mensaje);
                }
            }
            catch (TimeoutException)
            {

            }
            catch (Exception e)
            {
                colaMensaje.Enqueue("SYS_ERROR: " + e.Message);
                leyendo = false;
            }
        }
    }

    private void ProcesarMensaje(string mensaje)
    {
        if (mensaje == "READY")
        {
            Debug.Log("Arduino y MPR121 iniciado correctamente");
            return;
        }
        if (mensaje == "ERROR_MPR121")
        {
            Debug.Log("Arduino no detectó al MPR121");
            return;
        }
        if (mensaje.StartsWith("SYS_ERROR"))
        {
            Conectado = false;
            Debug.Log("Error comunicación: " + mensaje);
            return;
        }
        string[] partes = mensaje.Split(',');
        if (partes.Length != 2)
        {
            Debug.LogWarning("Formato de mensajes desconocido " + mensaje);
            return;
        }
    }

}
