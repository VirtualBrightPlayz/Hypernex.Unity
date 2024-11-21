using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hypernex.CCK;
using Hypernex.Tools;
using Unity.Entities.UniversalDelegates;
using UnityEngine;
using Logger = Hypernex.CCK.Logger;

namespace Hypernex.Game.Audio
{
    [RequireComponent(typeof(AudioSource))]
    public class BufferAudioSource : MonoBehaviour
    {
        public const int SAMPLE_SIZE = 512;
        public const int CLIP_SAMPLE_SIZE = 4096;
        public const float MAX_DELAY = 0.1f;
        private float[] RingBuffer = null;
        private int RingBufferPosition = 0;

        private bool shouldStop = false;
        private int stopTime = 0;
        private int lastTimeSamples = 0;
        private int maxEmptyReads = 0;
        private AudioClip clip = null;
        public AudioSource audioSource;

        private float[] spectrum = new float[1024];

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
        }

        private void Start()
        {
            audioSource = GetComponent<AudioSource>();
        }

        private void Update()
        {
            if (clip == null)
                return;
            clip.SetData(RingBuffer, 0);
            int currentDeltaSamples = audioSource.timeSamples - lastTimeSamples;
            if (audioSource.timeSamples < lastTimeSamples)
                currentDeltaSamples = clip.samples - audioSource.timeSamples;
            stopTime -= currentDeltaSamples;
            if (stopTime <= 0)
                shouldStop = true;
            lastTimeSamples = audioSource.timeSamples;

            audioSource.GetSpectrumData(spectrum, 0, FFTWindow.Rectangular);
            for (int i = 1; i < spectrum.Length - 1; i++)
            {
                Debug.DrawLine(new Vector3(i - 1, spectrum[i] + 10, 0), new Vector3(i, spectrum[i + 1] + 10, 0), Color.red);
                Debug.DrawLine(new Vector3(i - 1, Mathf.Log(spectrum[i - 1]) + 10, 2), new Vector3(i, Mathf.Log(spectrum[i]) + 10, 2), Color.cyan);
                Debug.DrawLine(new Vector3(Mathf.Log(i - 1), spectrum[i - 1] - 10, 1), new Vector3(Mathf.Log(i), spectrum[i] - 10, 1), Color.green);
                Debug.DrawLine(new Vector3(Mathf.Log(i - 1), Mathf.Log(spectrum[i - 1]), 3), new Vector3(Mathf.Log(i), Mathf.Log(spectrum[i]), 3), Color.blue);
            }

            if (shouldStop && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
        }

        private void AddRingBuffer(float[] pcm)
        {
            if (RingBuffer == null)
                return;
            stopTime += pcm.Length;
            for (int i = 0; i < pcm.Length; i++)
            {
                RingBuffer[RingBufferPosition] = pcm[i];
                RingBufferPosition = (RingBufferPosition + 1) % RingBuffer.Length;
            }
        }

        public void AddQueue(float[] pcm, int channels, int frequency)
        {
            bool newClip = false;
            if (clip == null || clip.channels != channels || clip.frequency != frequency)
            {
                maxEmptyReads = frequency * channels;
                newClip = true;
            }
            AddRingBuffer(pcm);
            if (newClip)
            {
                shouldStop = false;
                clip = AudioClip.Create("BufferAudio", maxEmptyReads, channels, frequency, false);
                RingBuffer = new float[clip.samples];
                audioSource.clip = clip;
                audioSource.loop = true;
                audioSource.Stop();
                RingBufferPosition = 0;
                stopTime = pcm.Length;
                AddRingBuffer(pcm);
                clip.SetData(RingBuffer, 0);
                audioSource.Play();
            }
            if (shouldStop && !audioSource.isPlaying)
            {
                shouldStop = false;
                RingBuffer = new float[clip.samples];
                audioSource.Stop();
                RingBufferPosition = 0;
                stopTime = pcm.Length;
                AddRingBuffer(pcm);
                clip.SetData(RingBuffer, 0);
                audioSource.Play();
            }
        }
    }
}