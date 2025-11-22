using UnityEngine;
using TMPro;

[ExecuteAlways]
[RequireComponent(typeof(TMP_Text))]
public class CurvedText : MonoBehaviour
{
    [Header("Curve Settings")]
    [SerializeField] float radius = 50f;
    [SerializeField] [Range(-180f, 180f)] float arcDegrees = 90f;
    [SerializeField] bool curveDownward = true;
    
    TMP_Text textComponent;
    bool isApplyingCurve = false; // prevent recursion
    
    void OnEnable()
    {
        textComponent = GetComponent<TMP_Text>();
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
    }
    
    void OnDisable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
    }
    
    void OnTextChanged(Object obj)
    {
        if (obj == textComponent && !isApplyingCurve)
        {
            ApplyCurve();
        }
    }
    
    void Update()
    {
        if (transform.hasChanged && !isApplyingCurve)
        {
            ApplyCurve();
            transform.hasChanged = false;
        }
    }
    
    void ApplyCurve()
    {
        if (textComponent == null || isApplyingCurve) return;
        
        isApplyingCurve = true;
        
        textComponent.ForceMeshUpdate();
        
        TMP_TextInfo textInfo = textComponent.textInfo;
        int characterCount = textInfo.characterCount;
        
        if (characterCount == 0)
        {
            isApplyingCurve = false;
            return;
        }
        
        // convert arc degrees to radians
        float arcAngle = Mathf.Deg2Rad * arcDegrees;
        
        // get text bounds
        Bounds bounds = textComponent.bounds;
        float textWidth = bounds.size.x;
        
        if (textWidth == 0)
        {
            isApplyingCurve = false;
            return;
        }
        
        for (int i = 0; i < characterCount; i++)
        {
            if (!textInfo.characterInfo[i].isVisible) continue;
            
            int vertexIndex = textInfo.characterInfo[i].vertexIndex;
            int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;
            Vector3[] vertices = textInfo.meshInfo[materialIndex].vertices;
            
            // get character position relative to text bounds
            Vector3 charMidpoint = (vertices[vertexIndex + 0] + vertices[vertexIndex + 2]) / 2f;
            
            // calculate normalized position (-0.5 to 0.5) relative to text center
            float normalizedX = (charMidpoint.x - bounds.center.x) / textWidth;
            
            // calculate angle for this character
            float charAngle = normalizedX * arcAngle;
            
            // calculate position on arc
            float x = radius * Mathf.Sin(charAngle);
            float y = radius * (1f - Mathf.Cos(charAngle));
            
            // flip for downward curve
            if (curveDownward)
                y = -y;
            
            // offset from straight line to arc
            Vector3 offset = new Vector3(x - normalizedX * textWidth, y, 0);
            
            // apply to all 4 vertices of the character
            for (int j = 0; j < 4; j++)
            {
                Vector3 vertOffset = vertices[vertexIndex + j] - charMidpoint;
                
                // rotate vertex offset by the angle
                float cos = Mathf.Cos(charAngle);
                float sin = Mathf.Sin(charAngle);
                
                if (curveDownward)
                    sin = -sin;
                
                Vector3 rotatedOffset = new Vector3(
                    vertOffset.x * cos - vertOffset.y * sin,
                    vertOffset.x * sin + vertOffset.y * cos,
                    vertOffset.z
                );
                
                vertices[vertexIndex + j] = charMidpoint + offset + rotatedOffset;
            }
        }
        
        // update the mesh WITHOUT triggering events
        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
            textComponent.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
        }
        
        isApplyingCurve = false;
    }
}