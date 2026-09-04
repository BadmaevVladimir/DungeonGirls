using NUnit.Framework;
using UnityEngine;

public class TaggedAudioTests
{
    [Test]
    public void PlayOneShot_NullSource_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => TaggedAudio.PlayOneShot(null, AudioClip.Create("clip", 1, 1, 44100, false), AudioCategory.SFX));
    }

    [Test]
    public void PlayOneShot_NullClip_DoesNotThrow()
    {
        var go = new GameObject("TaggedAudioTestHost");
        var source = go.AddComponent<AudioSource>();

        Assert.DoesNotThrow(() => TaggedAudio.PlayOneShot(source, null, AudioCategory.SFX));

        Object.DestroyImmediate(go);
    }

    [Test]
    public void Play_AssignsClipAndResetsTime_SoCallerCanSeekAfterwards()
    {
        var go = new GameObject("TaggedAudioTestHost");
        var source = go.AddComponent<AudioSource>();
        var clip = AudioClip.Create("clip", 44100 * 2, 1, 44100, false);

        TaggedAudio.Play(source, clip, AudioCategory.SFX);

        Assert.AreEqual(clip, source.clip);
        Assert.AreEqual(0f, source.time);

        Object.DestroyImmediate(go);
    }

    [Test]
    public void Play_NullSource_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => TaggedAudio.Play(null, AudioClip.Create("clip", 1, 1, 44100, false), AudioCategory.SFX));
    }
}
