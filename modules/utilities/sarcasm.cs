using System;

namespace atfot.utils;

public static class Sarcasm
{
    private static readonly string[] _remarks = {
        "Oh wow, you actually managed to type that correctly. I'm impressed.",
        "Congratulations. You've unlocked the power of... a functioning bot. Don't break it.",
        "Sure, let me fetch that for you. *eyeroll*",
        "Another command? Don't you have anything better to do?",
        "Wow, you're really good at typing `/` commands. Want a medal?",
        "I live to serve... unfortunately.",
        "Processing your request. Try not to hold your breath.",
        "You really think I needed that key? Fine, whatever.",
        "There. Happy now?",
        "I'm only doing this because I have to.",
        "Brilliant move. Absolutely genius.",
        "Your intelligence is truly... something.",
        "I'd clap, but my hands are busy typing your results.",
        "This is fine. Everything is fine.",
        "Oh, you wanted that? Could have just asked nicely. Oh wait, you did."
    };
    private static readonly Random _rand = new();

    public static string Get() => _remarks[_rand.Next(_remarks.Length)];
}
