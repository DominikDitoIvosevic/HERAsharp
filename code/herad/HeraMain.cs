﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace herad
{
    public static class HeraMain
    {
        public static void Run(string readsPath, string contigsPath, string readToReadPath, string readToContigPath)
        {
            (List<Seq> aSeqs, Dictionary<string, List<Overlap>> allOverlaps) = InitializationCode.SetupVariables(readsPath, contigsPath, readToReadPath, readToContigPath);

            MainLogic(aSeqs, allOverlaps);
        }

        private static void MainLogic(List<Seq> aSeqs, Dictionary<string, List<Overlap>> allOverlaps)
        {
            // GENERATION OF CONSENSUS SEQUENCES
            List<(string, string, List<OverlapPath>, OverlapPath)> listOfConsensuses = GetConsensusSequences(aSeqs, allOverlaps);

            Console.WriteLine("Got consensuses");

            // CONSTRUCTION OF OVERLAP GRAPH
            List<(string, string)> graph = GetConnectionGraph(listOfConsensuses);

            Console.WriteLine("Got connection graph");

            // FINAL SEQUENCE ASSEMBLY
            List<Overlap> completePath = GetFinalPathOverlaps(listOfConsensuses, graph);

            Console.WriteLine("Got final path overlaps");

            Console.WriteLine(string.Join("", completePath.Select(t => "(" + t.QuerySeqName + "," + t.TargetSeqName + ")")));

            string final = BuildSequenceFromOverlaps(completePath);

            string folder = @"C:\git\HERAsharp\code\data\ec_test2";
            File.WriteAllText(Path.Join(folder, "complete.fasta"), ">finalsequence" + Environment.NewLine + final);
        }

        private static string BuildSequenceFromOverlaps(List<Overlap> completePath)
        {
            StringBuilder sb = new StringBuilder();
            bool firstStrand = true;

            // QUERY 0 ITERATOR
            int q0It = 0;

            // OVERLAP END IT
            int oEIt = 0;

            for (int i = 0; i < completePath.Count; ++i)
            {
                var ol = completePath[i];
                var leftSeq = OverlapPath.AllSeqs[ol.QuerySeqName];

                var queryStart = ol.QueryStartCoord;
                var targetStart = ol.TargetStartCoord;
                var queryEnd = ol.QueryEndCoord;
                var sameStrand = ol.SameStrand;
                //var targetEnd = ol.TargetEndCoord;

                if (!sameStrand && !firstStrand)
                {
                    queryStart = ol.QuerySeqLen - ol.QueryEndCoord;
                    queryEnd = ol.QuerySeqLen - ol.QueryStartCoord;
                }else if (sameStrand && !firstStrand) {

                    queryStart = ol.QuerySeqLen - ol.QueryEndCoord;
                    queryEnd = ol.QuerySeqLen - ol.QueryStartCoord;
                    targetStart = ol.TargetSeqLen - ol.TargetEndCoord;
                }
                else if (!sameStrand && firstStrand)
                {
                    targetStart = ol.TargetSeqLen - ol.TargetEndCoord;
                }

                int startDiff = queryStart - targetStart;


                //if (ol.SameStrand && !firstStrand)
                //{
                //    startDiff = -startDiff;
                //    ol = ol.GetFlipped();
                //}


                var absQEnd = q0It + queryEnd;

                if (absQEnd > oEIt)
                {
                    int addedLen = absQEnd - oEIt;
                    int posOnLeftStrand = oEIt - q0It;
                    sb.Append(leftSeq.Content.Flip(firstStrand).Substring(posOnLeftStrand, addedLen));
                    oEIt = absQEnd;
                }

                if (sameStrand == false) firstStrand = !firstStrand;
                q0It += startDiff;
            }

            var lastOl = completePath.Last();
            var lastTargetStart = q0It + lastOl.QueryStartCoord - lastOl.TargetStartCoord;
            var lastTargetTotalEnd = lastTargetStart + lastOl.TargetSeqLen;
            if (lastTargetTotalEnd - oEIt > 0)
            {
                var rightSeq = OverlapPath.AllSeqs[lastOl.TargetSeqName];
                sb.Append(rightSeq.Content.Substring(lastOl.TargetEndCoord));
            }

            string final = sb.ToString();
            return final;
        }

        private static List<(string, string, List<OverlapPath>, OverlapPath)> GetConsensusSequences(List<Seq> aSeqs, Dictionary<string, List<Overlap>> allOverlaps)
        {
            ConcurrentBag<(string, string, List<OverlapPath>, OverlapPath)> listOfConsensuses = new ConcurrentBag<(string, string, List<OverlapPath>, OverlapPath)>();

            var options = new ParallelOptions();
            options.MaxDegreeOfParallelism = 1;

            Parallel.ForEach(aSeqs, options, ctg =>
            {
                var firstContigOverlaps = allOverlaps[ctg.Name];

                // FINDING PATHS BETWEEN ANCHORING NODES IN HERA

                // APPROACH I
                List<OverlapPath> pathsUsingOverlapScore = GetPathsUsingStrategy(allOverlaps, firstContigOverlaps, Strategies.GetBestOverlapByOverlapScore);

                // APPROACH II
                List<OverlapPath> pathsUsingExtensionScore = GetPathsUsingStrategy(allOverlaps, firstContigOverlaps, Strategies.GetBestOverlapByExtensionScore);

                // APPROACH III
                //List<Path> pathsUsingMonteCarlo = GetPathsUsingStrategy(allOverlaps, firstContigOverlaps, Strategies.GetBestOverlapByMonteCarlo);

                var allPaths = pathsUsingOverlapScore.Concat(pathsUsingExtensionScore);

                List<(string Key, List<OverlapPath>)> byEndContig = pathsUsingOverlapScore.GroupBy(p => p.Overlaps.Last().TargetSeqName).Select(g => (g.Key, g.ToList())).Where(g => g.Key != ctg.Name).ToList();

                foreach (var keyCtg in byEndContig)
                {
                    var consensus = GetConsensusSequenceAndItsGroup(keyCtg.Item2);

                    listOfConsensuses.Add((ctg.Name, keyCtg.Key, consensus.Item2, consensus.Item1));
                }
            });

            return listOfConsensuses.Reverse().ToList();
        }

        private static List<Overlap> GetFinalPathOverlaps(List<(string, string, List<OverlapPath>, OverlapPath)> listOfConsensuses, List<(string, string)> graph)
        {
            var firstCtg = graph.First().Item1;
            var realFirst = firstCtg;

            while (true)
            {
                var newFirst = graph.FirstOrDefault(n => n.Item2 == firstCtg);
                if (newFirst == default || realFirst == newFirst.Item1) break;
                firstCtg = newFirst.Item1;
            }

            var ordered = new List<string> { firstCtg };

            while (true)
            {
                var next = graph.FirstOrDefault(n => n.Item1 == ordered.Last());
                if (next == default || next.Item2 == firstCtg) break;
                ordered.Add(next.Item2);
            }

            List<(string, string, List<OverlapPath>, OverlapPath)> orderedPairs = ordered.Take(ordered.Count-1).Zip(ordered.Skip(1), (a,b) => (a,b)).Select(p => listOfConsensuses.First(c => c.Item1 == p.a && c.Item2 == p.b)).ToList();

            List<Overlap> completePath = new List<Overlap>();

            foreach (var c in orderedPairs)
            {
                completePath.AddRange(c.Item4.Overlaps);
            }

            return completePath;
        }

        private static List<(string, string)> GetConnectionGraph(List<(string, string, List<OverlapPath>, OverlapPath)> listOfConsensuses)
        {
            var graph = new List<(string, string)>();
            var ordered = listOfConsensuses.OrderByDescending(g => g.Item3.Count);

            foreach ((string leftCtg, string rightCtg, List<OverlapPath> paths, OverlapPath conensus) in ordered)
            {

                if (graph.Contains((leftCtg, rightCtg)) || graph.Contains((leftCtg, rightCtg))) continue;
                if (leftCtg == rightCtg) continue;

                bool areConnected = false;
                var connected = new HashSet<string> { leftCtg };
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

        private static (OverlapPath, List<OverlapPath>) GetConsensusSequenceAndItsGroup(List<OverlapPath> pathsUsingOverlapScore)
        {
            var byLen = pathsUsingOverlapScore.Select(o => (o.GetPathLength(), o)).OrderByDescending(p => p.Item1).ToList();

            var by1000 = byLen.GroupBy(lp => lp.Item1 / (1 * 1000 * 1000)).Select(g => (g.Count(), g.ToList())).ToList();

            (int, List<(int, OverlapPath o)>) bestGroup = by1000.OrderByDescending(g => g.Item1).First();

            int indexOfBest = by1000.IndexOf(bestGroup);


            var inBestByScore = bestGroup.Item2.Select(p => (p.o.Overlaps.Sum(oo => oo.OverlapScore) / p.o.Overlaps.Count(), p.o)).OrderByDescending(p => p.Item1).ToList();
            var consensusSequence = inBestByScore.First().o;
            return (consensusSequence, bestGroup.Item2.Select(g => g.o).ToList());
        }

        private static List<OverlapPath> GetPathsUsingStrategy(
            Dictionary<string, List<Overlap>> overlapsDict,
            List<Overlap> firstContigOverlaps,
            Func<OverlapPath, List<Overlap>, Overlap> getBest
            )
        {
            var queueOfInterestingOverlaps = new Queue<OverlapPath>(firstContigOverlaps.Select(q => new OverlapPath(q)));

            //Debug.Assert(queueOfInterestingOverlaps.First().ToSkip.Count() > overlapsDict.Max(o => o.Value.Count));

            List<OverlapPath> finalized = new List<OverlapPath>();

            while (queueOfInterestingOverlaps.Any())
            {
                OverlapPath nexty = queueOfInterestingOverlaps.Dequeue();
                var lastOver = nexty.Overlaps.Last();

                var neighbourOverlaps = overlapsDict[lastOver.TargetSeqName];
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

                nexty.ToSkip.Add(best.TargetSeqName);
                nexty.AddOverlap(best);

                // If best option is to connect the read to contig
                if (best.TargetSeqName.StartsWith("ctg", StringComparison.OrdinalIgnoreCase)) // TODO: Find better way to ensure the target is contig
                {
                    finalized.Add(nexty);
                    continue; // This path is complete and don't process it in the queue anymore
                }

                queueOfInterestingOverlaps.Enqueue(nexty);
            }

            return finalized.Where(p => p.Length > 0).ToList();
        }
    }
}
