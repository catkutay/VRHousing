using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
public class HMDInfoManager : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        Debug.Log("Active " + XRSettings.isDeviceActive);
        Debug.Log("Device " + XRSettings.loadedDeviceName);
    }

    // Update is called once per frame
    void Update()
    {
        if (XRSettings.loadedDeviceName=="MockHMDDisplay"){

            
        }
    }
}
