using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Reads the number a button's OnClick passes to a method, e.g. 4 from "Cursor.ResearchUnit(4)".
// Lets menus grey out or tint buttons by the unit they're for without any extra setup on each button,
// and it can never disagree with what the button actually does when clicked.
// Unity doesn't expose OnClick arguments publicly, so this reads the same fields the Inspector shows
// (m_PersistentCalls > m_Calls > m_Arguments > m_IntArgument, the names you can see in the scene file)
public static class ButtonOnClick
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

    // The int passed to methodName, or -1 if the button doesn't call it
    public static int IntArgument(Button button, string methodName)
    {
        UnityEvent onClick = button.onClick;
        for (int i = 0; i < onClick.GetPersistentEventCount(); i++)
        {
            if (onClick.GetPersistentMethodName(i) != methodName) continue;
            try
            {
                object calls = typeof(UnityEventBase).GetField("m_PersistentCalls", Hidden).GetValue(onClick);
                IList list = (IList)calls.GetType().GetField("m_Calls", Hidden).GetValue(calls);
                object arguments = list[i].GetType().GetField("m_Arguments", Hidden).GetValue(list[i]);
                return (int)arguments.GetType().GetField("m_IntArgument", Hidden).GetValue(arguments);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ButtonOnClick] Couldn't read the OnClick number on {button.name}: {e.Message}");
                return -1;
            }
        }
        return -1;
    }
}
