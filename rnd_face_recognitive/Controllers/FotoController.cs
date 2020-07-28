using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using rnd_face_recognitive.Models;
using Microsoft.Azure.CognitiveServices.Vision.Face;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
using System.IO;
using Microsoft.AspNetCore.Http;
using rnd_face_recognitive.Data;

namespace rnd_face_recognitive.Controllers
{
    public class FotoController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private PersonGroup personGroup;
        IFaceClient faceClient = Authenticate("https://westcentralus.api.cognitive.microsoft.com/", "406ba95805ca490facda8b6cd7f0381c");
        private readonly DataBaseContext _myDbContext = new DataBaseContext();
        public FotoController(ILogger<HomeController> logger)
        {
            _logger = logger;
            personGroup = new PersonGroup()
            {
                PersonGroupId = "gits",
                Name = "GITS",
                RecognitionModel = "recognition_02"
            };

            using (var listGroup = faceClient.PersonGroup.ListAsync())
            {
                if (!listGroup.Result.Contains(personGroup))
                {
                    faceClient.PersonGroup.CreateAsync(personGroup.PersonGroupId, personGroup.Name, recognitionModel: "recognition_02");
                }
            }
        }

        public static IFaceClient Authenticate(string endpoint, string key)
        {
            return new FaceClient(new ApiKeyServiceClientCredentials(key)) { Endpoint = endpoint };
        }

        public IActionResult TakeFoto()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CaptureAsync()
        {
            try
            {
                var files = HttpContext.Request.Form.Files;

                if (files != null)
                {
                    foreach (var file in files)
                    {
                        Guid unique = Guid.NewGuid();

                        var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot\\foto", unique.ToString() + ".jpeg");

                        var result = await faceClient.Face.DetectWithStreamAsync(file.OpenReadStream(), true, true, recognitionModel: "recognition_02", returnRecognitionModel: true);

                        if (result.Count > 0)
                        {
                            //register foto
                            var result2 = await faceClient.PersonGroupPerson.CreateAsync(personGroup.PersonGroupId, result[0].FaceId.Value.ToString());
                            var result3 = await faceClient.PersonGroupPerson.AddFaceFromStreamAsync(personGroup.PersonGroupId, result2.PersonId, file.OpenReadStream());

                            using (var stream = new FileStream(path, FileMode.Create))
                            {
                                file.CopyTo(stream);
                                //save database
                                _myDbContext.persons.Add(new Data.Person()
                                {
                                    Id = result2.PersonId,
                                    Path = Path.Combine("foto", unique + ".jpeg")
                                });
                                _myDbContext.SaveChanges();
                            }
                        }
                        else
                        {
                            return Json("Gagal upload, itu muka atau sendal jepit");
                        }
                    }
                }

                return Json("Sukses");
            }
            catch (Exception) { throw; }
        }

        public IActionResult CekMuka()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> PostCekMuka()
        {
            try
            {
                var files = HttpContext.Request.Form.Files;
                if (files != null)
                {
                    foreach (var file in files)
                    {
                        //train
                        await faceClient.PersonGroup.TrainAsync(personGroup.PersonGroupId);

                        TrainingStatus trainingStatus = null;
                        while (true)
                        {
                            trainingStatus = await faceClient.PersonGroup.GetTrainingStatusAsync(personGroup.PersonGroupId);

                            if (trainingStatus.Status != TrainingStatusType.Running)
                            {
                                break;
                            }

                            await Task.Delay(1000);
                        }

                        var faces = await faceClient.Face.DetectWithStreamAsync(file.OpenReadStream());
                        if (faces.Count > 0)
                        {
                            List<Guid> faceIds = faces.Select(face => face.FaceId.Value).ToList();
                            var results = await faceClient.Face.IdentifyAsync(faceIds, personGroup.PersonGroupId, maxNumOfCandidatesReturned: 5);
                            if (results.Count > 0)
                            {
                                foreach (var identifyResult in results)
                                {
                                    if (identifyResult.Candidates.Count == 0)
                                    {
                                        return Json("Muka kamu kesepian, take foto dulu sana");
                                    }
                                    else
                                    {
                                        var listFoto = _myDbContext.persons.Where(x => identifyResult.Candidates.Select(f => f.PersonId).ToList().Contains(x.Id)).ToList();
                                        return Json(string.Join(',', listFoto.Select(x=>x.Path).ToList()));
                                    }
                                }
                            }
                            else
                            {
                                return Json("Muka kamu kesepian, take foto dulu sana");
                            }
                        }
                        else
                        {
                            return Json("gagal, muka kamu kaya rambutan");
                        }
                    }
                }

                return Json("Foto kosong");
            }
            catch (Exception e)
            {
                throw;
            }
        }
    }
}
