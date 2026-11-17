using UnityEngine;

public class TimerScript : MonoBehaviour
{

    public float _timer;

    void Awake()
    {
        _timer = 0f;
    }
    
    void Update()
    {
        _timer += Time.deltaTime;
        Debug.Log(_timer + " " + Time.deltaTime);
    }
}
