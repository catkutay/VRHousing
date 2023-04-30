using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class activate : MonoBehaviour
{
    // Start is called before the first frame update
   
  private GameObject _button;
  void Start() 
  {
    _button = GameObject.Find("UI Sample");
  }

  void Update() 
  {
    if (Input.GetButtonDown("A")) {
      _button.SetActive(true);
    }
  
}

}
