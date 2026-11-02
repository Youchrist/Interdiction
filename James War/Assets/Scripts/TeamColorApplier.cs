using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class TeamColorApplier : MonoBehaviour
{
    [Tooltip("Sample this from the sprite's uniform using the color picker's eyedropper")]
    public Color targetColor = new Color(0.13f, 0.55f, 0.13f);

    [Header("Team Colors")]
    public Color playerColor = new Color(0.2f, 0.4f, 1f);
    public Color allyColor = new Color(1f, 0.85f, 0.1f);
    public Color enemyColor = new Color(0.85f, 0.15f, 0.15f);

    [Range(0f, 0.5f)] public float hueTolerance = 0.08f;
    [Range(0f, 1f)] public float minSaturation = 0.15f;

    private SpriteRenderer sr;
    private MaterialPropertyBlock mpb;

    public void ApplyTeamColor(bool isPlayer, bool isAlly, bool isEnemy)
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        if (mpb == null) mpb = new MaterialPropertyBlock();

        Color teamColor = isPlayer ? playerColor : isAlly ? allyColor : isEnemy ? enemyColor : Color.white;

        sr.GetPropertyBlock(mpb);
        mpb.SetColor("_TargetColor", targetColor);
        mpb.SetColor("_TeamColor", teamColor);
        mpb.SetFloat("_HueTolerance", hueTolerance);
        mpb.SetFloat("_MinSaturation", minSaturation);
        sr.SetPropertyBlock(mpb);
    }
}
