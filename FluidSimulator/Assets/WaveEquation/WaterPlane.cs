using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class WaterPlane : MonoBehaviour
{
    public static Transform s_Water;

    public static Transform s_Player;

    public Transform player;
    // Start is called before the first frame update
    void Start()
    {
        s_Player = player;
    }
    
    // Update is called once per frame
    void Update()
    {
        if (s_Water == null)
        {
            s_Water = transform;
        }
    }
}
