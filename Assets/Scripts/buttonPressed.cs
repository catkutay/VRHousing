using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class buttonPressed : MonoBehaviour
{
   public  GameObject gob;
   public Material brick, perforated_brick;
   public Material material;

    // Start is called before the first frame update
    void Awake()
    {
         material = this.gob.GetComponent<Renderer>().material;
     
    }

    // Update is called once per frame
    void Update()
    {
     
     }
    public void onPressed(){
         if (this.gob.GetComponent<MeshRenderer> ().material==material) this.gob.GetComponent<MeshRenderer> ().material= brick;
         else if (this.gob.GetComponent<MeshRenderer> ().material==brick)this.gob.GetComponent<MeshRenderer> ().material= perforated_brick;
         else this.gob.GetComponent<MeshRenderer> ().material=brick;
     }
    
}
