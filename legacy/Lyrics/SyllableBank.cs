namespace Spectralis;

/// <summary>
/// Syllable splitting for LRC word-timed lyrics.
/// Words can be manually split with a pipe character: hel|lo → ["hel", "lo"].
/// Known words are looked up in the built-in bank; unknowns return as a single syllable.
/// </summary>
internal static class SyllableBank
{
    /// <summary>
    /// Splits a word into syllables.
    /// Pipe characters in the input take precedence over the bank.
    /// Leading/trailing punctuation is preserved on the first/last syllable.
    /// </summary>
    public static string[] Split(string word)
    {
        if (string.IsNullOrEmpty(word))
            return [word];

        // Pipe override: "hel|lo" → ["hel", "lo"]
        if (word.Contains('|', StringComparison.Ordinal))
            return word.Split('|', StringSplitOptions.RemoveEmptyEntries);

        // Strip edge punctuation for lookup, then reattach.
        var start = 0;
        var end = word.Length - 1;
        while (start <= end && IsEdgePunct(word[start])) start++;
        while (end >= start && IsEdgePunct(word[end])) end--;

        if (start > end)
            return [word]; // all punctuation

        var core = word[start..(end + 1)];
        if (!Entries.TryGetValue(core, out var syllables))
            return [word]; // not in bank — single syllable

        // Reattach punctuation
        if (start == 0 && end == word.Length - 1)
            return syllables;

        var result = (string[])syllables.Clone();
        if (start > 0)
            result[0] = word[..start] + result[0];
        if (end < word.Length - 1)
            result[^1] += word[(end + 1)..];
        return result;
    }

    private static bool IsEdgePunct(char c) =>
        c is '.' or ',' or '!' or '?' or ':' or ';' or '"' or '\'' or '(' or ')' or '…' or '—' or '-' or '~';

    private static readonly Dictionary<string, string[]> Entries =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // A
            { "about",       ["a", "bout"] },
            { "above",       ["a", "bove"] },
            { "across",      ["a", "cross"] },
            { "after",       ["af", "ter"] },
            { "again",       ["a", "gain"] },
            { "against",     ["a", "gainst"] },
            { "almost",      ["al", "most"] },
            { "alone",       ["a", "lone"] },
            { "along",       ["a", "long"] },
            { "already",     ["al", "read", "y"] },
            { "also",        ["al", "so"] },
            { "although",    ["al", "though"] },
            { "always",      ["al", "ways"] },
            { "amazing",     ["a", "maz", "ing"] },
            { "another",     ["a", "noth", "er"] },
            { "answer",      ["an", "swer"] },
            { "anyone",      ["an", "y", "one"] },
            { "anything",    ["an", "y", "thing"] },
            { "anyway",      ["an", "y", "way"] },
            { "anywhere",    ["an", "y", "where"] },
            { "apart",       ["a", "part"] },
            { "around",      ["a", "round"] },
            { "arriving",    ["ar", "riv", "ing"] },
            { "artist",      ["art", "ist"] },
            { "asking",      ["ask", "ing"] },
            { "asleep",      ["a", "sleep"] },
            { "away",        ["a", "way"] },
            // B
            { "baby",        ["ba", "by"] },
            { "barely",      ["bare", "ly"] },
            { "battle",      ["bat", "tle"] },
            { "because",     ["be", "cause"] },
            { "become",      ["be", "come"] },
            { "before",      ["be", "fore"] },
            { "begin",       ["be", "gin"] },
            { "beginning",   ["be", "gin", "ning"] },
            { "behind",      ["be", "hind"] },
            { "believe",     ["be", "lieve"] },
            { "believing",   ["be", "liev", "ing"] },
            { "belong",      ["be", "long"] },
            { "belonging",   ["be", "long", "ing"] },
            { "below",       ["be", "low"] },
            { "beneath",     ["be", "neath"] },
            { "beside",      ["be", "side"] },
            { "between",     ["be", "tween"] },
            { "better",      ["bet", "ter"] },
            { "beyond",      ["be", "yond"] },
            { "body",        ["bod", "y"] },
            { "breaking",    ["break", "ing"] },
            { "broken",      ["bro", "ken"] },
            { "burning",     ["burn", "ing"] },
            // C
            { "calling",     ["call", "ing"] },
            { "carry",       ["car", "ry"] },
            { "carrying",    ["car", "ry", "ing"] },
            { "changing",    ["chang", "ing"] },
            { "chasing",     ["chas", "ing"] },
            { "chorus",      ["cho", "rus"] },
            { "city",        ["ci", "ty"] },
            { "closer",      ["clos", "er"] },
            { "c'mon",       ["c'", "mon"] },
            { "comin'",      ["com", "in'"] },
            { "coming",      ["com", "ing"] },
            { "couldn't",    ["could", "n't"] },
            { "counting",    ["count", "ing"] },
            { "crazy",       ["cra", "zy"] },
            { "crying",      ["cry", "ing"] },
            // D
            { "dancing",     ["danc", "ing"] },
            { "darkness",    ["dark", "ness"] },
            { "decide",      ["de", "cide"] },
            { "deeper",      ["deep", "er"] },
            { "desire",      ["de", "sire"] },
            { "different",   ["dif", "fer", "ent"] },
            { "didn't",      ["did", "n't"] },
            { "doing",       ["do", "ing"] },
            { "doesn't",     ["does", "n't"] },
            { "dreaming",    ["dream", "ing"] },
            { "driven",      ["driv", "en"] },
            { "drowning",    ["drown", "ing"] },
            { "dying",       ["dy", "ing"] },
            // E
            { "early",       ["ear", "ly"] },
            { "easy",        ["ea", "sy"] },
            { "either",      ["ei", "ther"] },
            { "empty",       ["emp", "ty"] },
            { "ending",      ["end", "ing"] },
            { "enter",       ["en", "ter"] },
            { "even",        ["e", "ven"] },
            { "every",       ["ev", "ery"] },
            { "everyone",    ["ev", "ery", "one"] },
            { "everything",  ["ev", "ery", "thing"] },
            { "everywhere",  ["ev", "ery", "where"] },
            // F
            { "fallen",      ["fall", "en"] },
            { "falling",     ["fall", "ing"] },
            { "famous",      ["fa", "mous"] },
            { "farther",     ["far", "ther"] },
            { "faster",      ["fast", "er"] },
            { "feelin'",     ["feel", "in'"] },
            { "feeling",     ["feel", "ing"] },
            { "fighting",    ["fight", "ing"] },
            { "finding",     ["find", "ing"] },
            { "flying",      ["fly", "ing"] },
            { "follow",      ["fol", "low"] },
            { "forever",     ["for", "ev", "er"] },
            { "forget",      ["for", "get"] },
            { "forgetting",  ["for", "get", "ting"] },
            { "forgive",     ["for", "give"] },
            { "forgiving",   ["for", "giv", "ing"] },
            { "forgotten",   ["for", "got", "ten"] },
            { "forward",     ["for", "ward"] },
            { "freedom",     ["free", "dom"] },
            // G
            { "gimme",       ["gim", "me"] },
            { "giving",      ["giv", "ing"] },
            { "glory",       ["glo", "ry"] },
            { "going",       ["go", "ing"] },
            { "golden",      ["gold", "en"] },
            { "gonna",       ["gon", "na"] },
            { "gotten",      ["got", "ten"] },
            { "gotta",       ["got", "ta"] },
            { "gravity",     ["grav", "i", "ty"] },
            // H
            { "hadn't",      ["had", "n't"] },
            { "happened",    ["hap", "pened"] },
            { "happening",   ["hap", "pen", "ing"] },
            { "happy",       ["hap", "py"] },
            { "harder",      ["hard", "er"] },
            { "hasn't",      ["has", "n't"] },
            { "haven't",     ["have", "n't"] },
            { "heaven",      ["heav", "en"] },
            { "heavy",       ["heav", "y"] },
            { "hello",       ["hel", "lo"] },
            { "hidden",      ["hid", "den"] },
            { "higher",      ["high", "er"] },
            { "holding",     ["hold", "ing"] },
            { "homie",       ["ho", "mie"] },
            { "honey",       ["hon", "ey"] },
            { "honor",       ["hon", "or"] },
            { "hoping",      ["hop", "ing"] },
            { "however",     ["how", "ev", "er"] },
            { "hunger",      ["hun", "ger"] },
            { "hurricane",   ["hur", "ri", "cane"] },
            { "hurting",     ["hurt", "ing"] },
            // I
            { "illusion",    ["il", "lu", "sion"] },
            { "imagine",     ["i", "mag", "ine"] },
            { "instead",     ["in", "stead"] },
            { "inside",      ["in", "side"] },
            { "into",        ["in", "to"] },
            { "isn't",       ["is", "n't"] },
            // J
            { "jealousy",    ["jeal", "ous", "y"] },
            { "journey",     ["jour", "ney"] },
            // K
            { "keeping",     ["keep", "ing"] },
            { "kinda",       ["kin", "da"] },
            { "killing",     ["kill", "ing"] },
            { "knowing",     ["know", "ing"] },
            // L
            { "lately",      ["late", "ly"] },
            { "later",       ["la", "ter"] },
            { "laughing",    ["laugh", "ing"] },
            { "laying",      ["lay", "ing"] },
            { "leading",     ["lead", "ing"] },
            { "leaving",     ["leav", "ing"] },
            { "lemme",       ["lem", "me"] },
            { "listen",      ["lis", "ten"] },
            { "little",      ["lit", "tle"] },
            { "living",      ["liv", "ing"] },
            { "lonely",      ["lone", "ly"] },
            { "longer",      ["long", "er"] },
            { "looking",     ["look", "ing"] },
            { "losing",      ["los", "ing"] },
            { "lotta",       ["lot", "ta"] },
            { "louder",      ["loud", "er"] },
            { "loving",      ["lov", "ing"] },
            { "lovin'",      ["lov", "in'"] },
            { "lucky",       ["luck", "y"] },
            { "lyrics",      ["lyr", "ics"] },
            // M
            { "making",      ["mak", "ing"] },
            { "maybe",       ["may", "be"] },
            { "melody",      ["mel", "o", "dy"] },
            { "memory",      ["mem", "o", "ry"] },
            { "mirror",      ["mir", "ror"] },
            { "missing",     ["miss", "ing"] },
            { "moment",      ["mo", "ment"] },
            { "money",       ["mon", "ey"] },
            { "morning",     ["morn", "ing"] },
            { "moving",      ["mov", "ing"] },
            { "music",       ["mu", "sic"] },
            { "myself",      ["my", "self"] },
            { "mystery",     ["mys", "ter", "y"] },
            // N
            { "never",       ["nev", "er"] },
            { "nobody",      ["no", "bod", "y"] },
            { "nothing",     ["noth", "ing"] },
            { "nothin'",     ["noth", "in'"] },
            { "notice",      ["no", "tice"] },
            { "nowhere",     ["no", "where"] },
            // O
            { "often",       ["of", "ten"] },
            { "only",        ["on", "ly"] },
            { "open",        ["o", "pen"] },
            { "other",       ["oth", "er"] },
            { "outta",       ["out", "ta"] },
            { "over",        ["o", "ver"] },
            { "overcome",    ["o", "ver", "come"] },
            // P
            { "paper",       ["pa", "per"] },
            { "paradise",    ["par", "a", "dise"] },
            { "passion",     ["pas", "sion"] },
            { "people",      ["peo", "ple"] },
            { "perfect",     ["per", "fect"] },
            { "perilous",    ["per", "i", "lous"] },
            { "picture",     ["pic", "ture"] },
            { "playing",     ["play", "ing"] },
            { "power",       ["pow", "er"] },
            { "powerful",    ["pow", "er", "ful"] },
            { "pretty",      ["pret", "ty"] },
            { "promise",     ["prom", "ise"] },
            { "protect",     ["pro", "tect"] },
            { "pushing",     ["push", "ing"] },
            { "putting",     ["put", "ting"] },
            // R
            { "racing",      ["rac", "ing"] },
            { "reaching",    ["reach", "ing"] },
            { "ready",       ["read", "y"] },
            { "realize",     ["re", "al", "ize"] },
            { "reason",      ["rea", "son"] },
            { "remember",    ["re", "mem", "ber"] },
            { "returning",   ["re", "turn", "ing"] },
            { "rising",      ["ris", "ing"] },
            { "river",       ["riv", "er"] },
            { "running",     ["run", "ning"] },
            { "runnin'",     ["run", "nin'"] },
            // S
            { "sadness",     ["sad", "ness"] },
            { "sailing",     ["sail", "ing"] },
            { "saving",      ["sav", "ing"] },
            { "saying",      ["say", "ing"] },
            { "searching",   ["search", "ing"] },
            { "seeing",      ["see", "ing"] },
            { "sending",     ["send", "ing"] },
            { "shadow",      ["shad", "ow"] },
            { "shaking",     ["shak", "ing"] },
            { "shattered",   ["shat", "tered"] },
            { "shouldn't",   ["should", "n't"] },
            { "silence",     ["si", "lence"] },
            { "simple",      ["sim", "ple"] },
            { "singing",     ["sing", "ing"] },
            { "sitting",     ["sit", "ting"] },
            { "slowly",      ["slow", "ly"] },
            { "somebody",    ["some", "bod", "y"] },
            { "somehow",     ["some", "how"] },
            { "someone",     ["some", "one"] },
            { "something",   ["some", "thing"] },
            { "somethin'",   ["some", "thin'"] },
            { "sometimes",   ["some", "times"] },
            { "somewhere",   ["some", "where"] },
            { "sorry",       ["sor", "ry"] },
            { "sorta",       ["sor", "ta"] },
            { "spirit",      ["spir", "it"] },
            { "standing",    ["stand", "ing"] },
            { "starting",    ["start", "ing"] },
            { "staying",     ["stay", "ing"] },
            { "story",       ["sto", "ry"] },
            { "stronger",    ["strong", "er"] },
            { "stupid",      ["stu", "pid"] },
            { "suddenly",    ["sud", "den", "ly"] },
            { "summer",      ["sum", "mer"] },
            { "surrender",   ["sur", "ren", "der"] },
            { "survive",     ["sur", "vive"] },
            { "system",      ["sys", "tem"] },
            // T
            { "taken",       ["tak", "en"] },
            { "taking",      ["tak", "ing"] },
            { "talking",     ["talk", "ing"] },
            { "telling",     ["tell", "ing"] },
            { "thinking",    ["think", "ing"] },
            { "together",    ["to", "geth", "er"] },
            { "tomorrow",    ["to", "mor", "row"] },
            { "tonight",     ["to", "night"] },
            { "trying",      ["try", "ing"] },
            { "turning",     ["turn", "ing"] },
            // U
            { "under",       ["un", "der"] },
            { "understand",  ["un", "der", "stand"] },
            { "universe",    ["u", "ni", "verse"] },
            { "until",       ["un", "til"] },
            { "upon",        ["u", "pon"] },
            { "using",       ["us", "ing"] },
            // V
            { "valley",      ["val", "ley"] },
            { "victory",     ["vic", "to", "ry"] },
            // W
            { "waiting",     ["wait", "ing"] },
            { "walking",     ["walk", "ing"] },
            { "wanna",       ["wan", "na"] },
            { "wanting",     ["want", "ing"] },
            { "warrior",     ["war", "ri", "or"] },
            { "wasn't",      ["was", "n't"] },
            { "watching",    ["watch", "ing"] },
            { "whatever",    ["what", "ev", "er"] },
            { "whenever",    ["when", "ev", "er"] },
            { "weren't",     ["were", "n't"] },
            { "wherever",    ["wher", "ev", "er"] },
            { "whisper",     ["whis", "per"] },
            { "whoever",     ["who", "ev", "er"] },
            { "winter",      ["win", "ter"] },
            { "wishing",     ["wish", "ing"] },
            { "within",      ["with", "in"] },
            { "without",     ["with", "out"] },
            { "woman",       ["wom", "an"] },
            { "women",       ["wom", "en"] },
            { "wonder",      ["won", "der"] },
            { "working",     ["work", "ing"] },
            { "wouldn't",    ["would", "n't"] },
            { "worship",     ["wor", "ship"] },
            { "writing",     ["writ", "ing"] },
            // Y
            { "yearning",    ["yearn", "ing"] },
            { "yesterday",   ["yes", "ter", "day"] },
            { "yourself",    ["your", "self"] },
        };
}
