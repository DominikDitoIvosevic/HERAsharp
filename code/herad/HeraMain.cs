﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace herad
{
    public static class HeraMain
    {
        public static void Run()
        {
            (List<Seq> aSeqs, Dictionary<int, List<Overlap>> allOverlaps) = InitializationCode.SetupVariables();

            MainLogic(aSeqs, allOverlaps);
        }

        private static void MainLogic(List<Seq> aSeqs, Dictionary<int, List<Overlap>> allOverlaps)
        {
            List<(int, int, List<Path>, Path)> listOfConsensuses = GetConsensusSequences(aSeqs, allOverlaps);
            List<(int, int)> graph = GetConnectionGraph(listOfConsensuses);
            List<Overlap> completePath = GetFinalPathOverlaps(listOfConsensuses, graph);

            StringBuilder sb = new StringBuilder();
            int iter = 0;
            int pathIter = 0;

            var firstOverlap = completePath.First();
            Seq firstSeq = Path.AllSeqs[">" + firstOverlap.QuerySeqName];
            sb.Append(firstSeq.Content.Substring(0, firstOverlap.QueryEndCoord));
            pathIter = firstOverlap.QueryEndCoord;

            bool firstStrand = true;

            for (int i = 0; i < completePath.Count; i++)
            {
                var ol = completePath[i];

                iter += ol.QueryStartCoord - ol.TargetStartCoord;
                if (pathIter < iter + ol.TargetEndCoord)
                {
                    var rightSeq = Path.AllSeqs[">" + ol.TargetSeqName];
                    int start = pathIter - iter;

                    if (start < 0)
                    {
                        var leftSeq = Path.AllSeqs[">" + ol.QuerySeqName];

                        var leftOfOverlap = -start + (ol.TargetStartCoord);
                        var queryStart = ol.QueryStartCoord - leftOfOverlap;

                        sb.Append(leftSeq.Content.Substring(queryStart, -start));
                        //pathIter += -start;
                        start = 0;
                    }

                    if (ol.SameStrand == false) firstStrand = !firstStrand;

                    int len = (ol.TargetEndCoord - start);
                    sb.Append(Flip(firstStrand, rightSeq.Content.Substring(start, len)));

                    if (i == completePath.Count - 1)
                    {
                        sb.Append(rightSeq.Content.Substring(start + len));
                    }

                    pathIter = iter + ol.TargetEndCoord;
                }
            }

            string final = sb.ToString();

            File.WriteAllText("complete.fasta", ">ditodito" + Environment.NewLine + final);
        }

        private static string Flip(bool firstStrand, string v)
        {
            if (firstStrand) return v;
            else return string.Join(string.Empty, v.Select(c =>
            {
                switch (c)
                {
                    case 'G': return 'C';
                    case 'C': return 'G';
                    case 'T': return 'A';
                    case 'A': return 'T';
                    default:
                        throw new NullReferenceException();
                }
            }));
        }

        private static List<(int, int, List<Path>, Path)> GetConsensusSequences(List<Seq> aSeqs, Dictionary<int, List<Overlap>> allOverlaps)
        {
            List<(int, int, List<Path>, Path)> listOfConsensuses = new List<(int, int, List<Path>, Path)>();

            foreach (var ctg in aSeqs)
            {
                var ctgName = int.Parse(ctg.Name.Substring(1 + "ctg".Length));

                var firstContigOverlaps = allOverlaps[ctgName];

                List<Path> pathsUsingOverlapScore = GetPathsUsingStrategy(allOverlaps, firstContigOverlaps, Strategies.GetBestOverlapByOverlapScore);

                //List<Path> pathsUsingExtensionScore = GetPathsUsingStrategy(allOverlaps, firstContigOverlaps, Strategies.GetBestOverlapByExtensionScore);
                //List<Path> pathsUsingMonteCarlo = GetPathsUsingStrategy(allOverlaps, firstContigOverlaps, Strategies.GetBestOverlapByMonteCarlo);

                List<(int Key, List<Path>)> byEndContig = pathsUsingOverlapScore.GroupBy(p => p.Overlaps.Last().TargetSeqCodename).Select(g => (g.Key, g.ToList())).Where(g => g.Key != ctgName).ToList();

                foreach (var keyCtg in byEndContig)
                {
                    var consensus = GetConsensusSequenceAndItsGroup(keyCtg.Item2);

                    listOfConsensuses.Add((ctgName, keyCtg.Key, consensus.Item2, consensus.Item1));
                }
            }

            return listOfConsensuses;
        }

        private static List<Overlap> GetFinalPathOverlaps(List<(int, int, List<Path>, Path)> listOfConsensuses, List<(int, int)> graph)
        {
            int firstCtg = graph.First().Item1;
            int realFirst = firstCtg;

            while (true)
            {
                var newFirst = graph.FirstOrDefault(n => n.Item2 == firstCtg);
                if (newFirst == default || realFirst == newFirst.Item1) break;
                firstCtg = newFirst.Item1;
            }

            var ordered = new List<int> { firstCtg };

            while (true)
            {
                var next = graph.FirstOrDefault(n => n.Item1 == ordered.Last());
                if (next == default || next.Item2 == firstCtg) break;
                ordered.Add(next.Item2);
            }

            List<(int, int, List<Path>, Path)> orderedPairs = ordered.SkipLast(1).Zip(ordered.Skip(1)).Select(p => listOfConsensuses.First(c => c.Item1 == p.First && c.Item2 == p.Second)).ToList();

            List<Overlap> completePath = new List<Overlap>();

            foreach (var c in orderedPairs)
            {
                completePath.AddRange(c.Item4.Overlaps);
            }

            return completePath;
        }

        private static List<(int, int)> GetConnectionGraph(List<(int, int, List<Path>, Path)> listOfConsensuses)
        {
            var graph = new List<(int, int)>();

            foreach ((int leftCtg, int rightCtg, List<Path> paths, Path conensus) in listOfConsensuses.OrderByDescending(g => g.Item3.Count))
            {

                if (graph.Contains((leftCtg, rightCtg)) || graph.Contains((leftCtg, rightCtg))) continue;
                if (leftCtg == rightCtg) continue;

                bool areConnected = false;
                var connected = new HashSet<int> { leftCtg };
                foreach (var node in graph)
                {
                    if (connected.Contains(node.Item1) && node.Item2 == rightCtg || connected.Contains(node.Item2) && node.Item1 == rightCtg)
                    {
                        areConnected = true;
                        break;
                    }

                    if (connected.Contains(node.Item1) && !connected.Contains(node.Item2)) connected.Add(node.Item2);
                    if (connected.Contains(node.Item2) && !connected.Contains(node.Item1)) connected.Add(node.Item1);
                }

                if (areConnected) continue;

                if (graph.Any(n => n.Item1 == leftCtg || n.Item2 == rightCtg)) continue;

                graph.Add((leftCtg, rightCtg));
            }

            return graph;
        }

        private static (Path, List<Path>) GetConsensusSequenceAndItsGroup(List<Path> pathsUsingOverlapScore)
        {
            var byLen = pathsUsingOverlapScore.Select(o => (o.GetPathLength(), o)).OrderByDescending(p => p.Item1).ToList();

            var groupSizes = byLen.Count() / 100;
            var groups = new List<List<Path>>();
            for (int i = 0; i < byLen.Count; i += groupSizes)
            {
                groups.Add(new List<Path>());
                for (int j = 0; j < groupSizes && i + j < byLen.Count; j++)
                {
                    groups.Last().Add(byLen[i + j].Item2);
                }
            }

            var by1000 = byLen.GroupBy(lp => lp.Item1 / (1 * 1000 * 1000)).Select(g => (g.Count(), g.ToList())).ToList();

            (int, List<(int, Path o)>) bestGroup = by1000.OrderByDescending(g => g.Item1).First();

            int indexOfBest = by1000.IndexOf(bestGroup);


            var inBestByScore = bestGroup.Item2.Select(p => (p.o.Overlaps.Sum(oo => oo.OverlapScore) / p.o.Overlaps.Count(), p.o)).OrderByDescending(p => p.Item1).ToList();
            var consensusSequence = inBestByScore.First().o;
            return (consensusSequence, bestGroup.Item2.Select(g => g.o).ToList());
        }

        private static List<Path> GetPathsUsingStrategy(
            Dictionary<int, List<Overlap>> overlapsDict,
            List<Overlap> firstContigOverlaps,
            Func<Path, List<Overlap>, Overlap> getBest
            )
        {
            var queueOfInterestingOverlaps = new Queue<Path>(firstContigOverlaps.Select(q => new Path(q)));

            Debug.Assert(queueOfInterestingOverlaps.First().ToSkip.Count() > overlapsDict.Max(o => o.Value.Count));

            List<Path> finalized = new List<Path>();

            while (queueOfInterestingOverlaps.Any())
            {
                Path nexty = queueOfInterestingOverlaps.Dequeue();
                var lastOver = nexty.Overlaps.Last();

                var neighbourOverlaps = overlapsDict[lastOver.TargetSeqCodename];
                // This time by overlap
                // Get first overlap which isn't already in NEXT
                // Order by overlap score and tie break on sequence identity
                Overlap best = getBest(nexty, neighbourOverlaps);

                // In case no valid next overlaps, backtrack
                if (best == default(Overlap))
                {
                    // If path only has 1 element, stop considering that path
                    if (nexty.Overlaps.Count() <= 1)
                    {
                        continue;
                    }

                    nexty.RemoveLastOverlapAndAddToSkipped();
                    queueOfInterestingOverlaps.Enqueue(nexty);
                    continue;
                }

                nexty.ToSkip[best.TargetSeqCodename] = true;
                nexty.AddOverlap(best);

                // If best option is to connect the read to contig
                if (best.TargetSeqCodename < 10) // TODO: Find better way to ensure the target is contig
                {
                    finalized.Add(nexty);
                    continue; // This path is complete and don't process it in the queue anymore
                }

                queueOfInterestingOverlaps.Enqueue(nexty);
            }

            return finalized.Where(p => p.Length > 10).ToList();
        }
    }
}
